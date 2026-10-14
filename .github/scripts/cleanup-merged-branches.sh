#!/usr/bin/env bash
# Delete remote branches whose pull request merged more than a week ago, when the
# branch has received nothing since. See .github/workflows/cleanup-branches.yml for
# why selection is by PR state rather than `git branch --merged`.
#
# A branch is deleted only when ALL of these hold:
#   - it is not long-lived (main, develop, staging, release-*);
#   - it has NO open pull request;
#   - a same-repository PR from it into a long-lived branch MERGED more than
#     CUTOFF_DAYS ago, and the branch head is EXACTLY that PR's head, so every
#     commit on it reached the target through that merge.
# Everything else is kept. DRY_RUN=true lists the decisions and deletes nothing.
#
# The workflow runs this as `bash <script>`: the executable bit is unreliable when
# committed from Windows, and its absence made every run exit 126.
#
# Env: REPO (owner/name), GH_TOKEN, DRY_RUN (true|false), CUTOFF_DAYS (default 7).
set -euo pipefail

: "${REPO:?REPO is required}"
DRY_RUN="${DRY_RUN:-false}"
CUTOFF_DAYS="${CUTOFF_DAYS:-7}"
CUTOFF="$(date -u -d "${CUTOFF_DAYS} days ago" +%Y-%m-%dT%H:%M:%SZ)"
PROTECTED='^(main|develop|staging|release-.*)$'

echo "Merged PRs whose branch is older than ${CUTOFF_DAYS} days (cutoff ${CUTOFF}), dry_run=${DRY_RUN}"

# Current remote heads, one lookup table for the whole run: name -> sha.
declare -A HEAD=()
while read -r sha ref; do
  HEAD["${ref#refs/heads/}"]="$sha"
done < <(git ls-remote --heads origin)
echo "remote branches: ${#HEAD[@]}"

# A read that silently returns nothing would make this a no-op that reports
# success -- the failure it replaces. A repository with branches has at least one.
if [ "${#HEAD[@]}" -eq 0 ]; then
  echo "::error::git ls-remote returned no branches; refusing to report a clean result."
  exit 1
fi

# Branches with an open PR are never touched, whatever their history.
OPEN_LIST="$(gh pr list --repo "$REPO" --state open --limit 1000 \
  --json headRefName,isCrossRepository \
  --jq '.[] | select(.isCrossRepository | not) | .headRefName')"
declare -A OPEN=()
while read -r b; do
  [ -n "$b" ] && OPEN["$b"]=1
done <<< "$OPEN_LIST"
echo "branches with an open PR: ${#OPEN[@]}"

# Only same-repository PRs merged INTO a long-lived branch count: a PR merged into
# another feature branch does not put its commits on develop/main, and a fork PR's
# headRefName names a branch in the fork, not here. --limit caps the window; a
# branch whose PR falls outside it is kept, never deleted.
MERGED="$(gh pr list --repo "$REPO" --state merged --limit 1000 \
  --json headRefName,headRefOid,mergedAt,number,baseRefName,isCrossRepository \
  --jq '.[] | select(.isCrossRepository | not)
             | select(.baseRefName | test("^(main|develop|staging|release-.*)$"))
             | "\(.headRefName)\t\(.headRefOid)\t\(.mergedAt)\t\(.number)"')"

declare -A DONE=()
deleted=0; kept_after=0; kept_recent=0; kept_open=0; failed=0
while IFS=$'\t' read -r branch pr_head merged_at number; do
  [ -z "$branch" ] && continue
  [[ "$branch" =~ $PROTECTED ]] && continue
  [ -n "${DONE[$branch]:-}" ] && continue
  cur="${HEAD[$branch]:-}"
  [ -z "$cur" ] && continue                     # already gone

  if [ -n "${OPEN[$branch]:-}" ]; then
    DONE[$branch]=1
    echo "keep   ${branch}  (has an open PR)"
    kept_open=$((kept_open+1))
    continue
  fi

  if [ "$cur" != "$pr_head" ]; then
    # Several PRs can share a branch name; only this row disagrees so far.
    # Decided below, once every merged PR for the name has been seen.
    continue
  fi
  DONE[$branch]=1

  if [[ "$merged_at" > "$CUTOFF" ]]; then
    echo "keep   ${branch}  (#${number} merged ${merged_at}, within ${CUTOFF_DAYS} days)"
    kept_recent=$((kept_recent+1))
    continue
  fi

  if [ "$DRY_RUN" = "true" ]; then
    echo "WOULD  delete ${branch}  (#${number} merged ${merged_at}, head ${cur:0:7} == PR head)"
  elif gh api -X DELETE "repos/${REPO}/git/refs/heads/${branch}" >/dev/null; then
    echo "delete ${branch}  (#${number} merged ${merged_at}, head ${cur:0:7})"
  else
    # e.g. a ruleset forbids deletion: report it, keep going, fail at the end.
    echo "::warning::could not delete ${branch} (#${number}); left in place"
    failed=$((failed+1))
    continue
  fi
  deleted=$((deleted+1))
done <<< "$MERGED"

# Branches with a merged PR whose head matched NONE of them: commits after merge.
while IFS=$'\t' read -r branch pr_head merged_at number; do
  [ -z "$branch" ] && continue
  [[ "$branch" =~ $PROTECTED ]] && continue
  [ -n "${DONE[$branch]:-}" ] && continue
  [ -z "${HEAD[$branch]:-}" ] && continue
  DONE[$branch]=1
  echo "keep   ${branch}  (head ${HEAD[$branch]:0:7} != merged PR head ${pr_head:0:7}: commits after merge, not in develop)"
  kept_after=$((kept_after+1))
done <<< "$MERGED"

verb=$([ "$DRY_RUN" = "true" ] && echo "would delete" || echo "deleted")
echo "::notice::${verb} ${deleted}; kept ${kept_open} with an open PR; kept ${kept_after} with commits after merge; kept ${kept_recent} merged within ${CUTOFF_DAYS} days; ${failed} deletion(s) failed."
if [ "$failed" -gt 0 ]; then
  echo "::error::${failed} branch deletion(s) failed."
  exit 1
fi
