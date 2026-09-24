#!/usr/bin/env bash
# Delete remote branches whose pull request merged more than a week ago, when the
# branch has received nothing since. See .github/workflows/cleanup-branches.yml for
# why selection is by PR state rather than `git branch --merged`.
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

MERGED="$(gh pr list --repo "$REPO" --state merged --limit 500 \
  --json headRefName,headRefOid,mergedAt,number \
  --jq '.[] | "\(.headRefName)\t\(.headRefOid)\t\(.mergedAt)\t\(.number)"')"

declare -A DONE=()
deleted=0; kept_after=0; kept_recent=0
while IFS=$'\t' read -r branch pr_head merged_at number; do
  [ -z "$branch" ] && continue
  [[ "$branch" =~ $PROTECTED ]] && continue
  [ -n "${DONE[$branch]:-}" ] && continue
  cur="${HEAD[$branch]:-}"
  [ -z "$cur" ] && continue                     # already gone

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
  else
    gh api -X DELETE "repos/${REPO}/git/refs/heads/${branch}" >/dev/null
    echo "delete ${branch}  (#${number} merged ${merged_at}, head ${cur:0:7})"
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
echo "::notice::${verb} ${deleted}; kept ${kept_after} with commits after merge; kept ${kept_recent} merged within ${CUTOFF_DAYS} days."
