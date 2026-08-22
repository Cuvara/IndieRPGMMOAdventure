#!/usr/bin/env bash
#
# Run N players against one backend and ASSERT the multi-client checklist,
# instead of asking a human to walk it.
#
# Why this exists: CLAUDE.md carries a table of seven things to check after
# launching three clients, and it was walked by hand. A checklist walked by hand
# is walked once. Worse, it was walked WRONG for as long as it existed -- one row
# told the operator to expect three `player:location:*` keys in Redis, and no
# code has ever written that key, so anyone following the table top to bottom hit
# zero on the last row and had every reason to call a healthy run failed. A row
# no machine checks is a row nobody notices is false.
#
# What this asserts, and what it deliberately does not:
#
#   ASSERTED  three distinct Nakama user ids            (shared identity evicts)
#   ASSERTED  every client reached IN WORLD             (join actually completed)
#   ASSERTED  no FATAL / unhandled exception in any log
#   ASSERTED  gameserver /status players_online == N
#   ASSERTED  every client was assigned the SAME server address   (ADR-2)
#   ASSERTED  Redis holds exactly N session keys
#   ASSERTED  Redis servers:map:<map> set holds exactly ONE member (ADR-2)
#   CAPTURED  one screenshot per window, for the one row a script cannot judge
#
# The last row is "each client's window shows N capsules, not one" -- the only
# row that actually proves mutual visibility. Judging it needs image analysis
# this script does not attempt, so it captures the windows and says plainly that
# a human must look. It does NOT quietly drop the row: a check that cannot run is
# reported as NOT CHECKED, never folded into a pass. Three clients that each see
# only themselves is a failure that looks exactly like success from every other
# row, which is why the honest gap matters more than the convenient silence.
#
# The screenshots use PrintWindow with PW_RENDERFULLCONTENT rather than raising
# each window and grabbing the screen. SetForegroundWindow fails from a
# background process, so the raise silently does nothing and the grab captures
# whatever is actually on top -- you get a screenshot of your own terminal that
# looks exactly like a captured game window. PrintWindow reads the window's own
# content while it stays occluded and works against this player's D3D12 surface.
#
# Note the PIDs run-clients.sh prints are the LAUNCHER's, not the player's, and
# have no window handle; the players are found by process name instead.
#
# Requires: kubectl (for the Redis assertions), curl, python3, powershell.exe.
# Redis assertions are skipped -- visibly -- when no kubectl context is given.

set -uo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

EXE=""
COUNT=3
GATEWAY_HOST="127.0.0.1"
GATEWAY_PORT=""
NAKAMA_HOST="127.0.0.1"
NAKAMA_PORT=""
NAKAMA_KEY=""
MAP_ID="map_01"
STATUS_URL=""
KUBE_CONTEXT=""
REDIS_NS="rpg-k8s-data"
REDIS_STS="redis"
SHOT_DIR=""
SETTLE=45
KEEP=0

usage() {
    cat <<'USAGE'
Usage: verify-multiclient.sh --exe <player.exe> --gateway-port P --nakama-port P
                             --nakama-key KEY --status-url URL [options]

  --exe PATH             Built Windows player. Required.
  --count N              Instances to launch (default 3).
  --gateway-host HOST    Default 127.0.0.1
  --gateway-port PORT    Required.
  --nakama-host HOST     Default 127.0.0.1
  --nakama-port PORT     Required.
  --nakama-key KEY       Required. NOT optional since the keys were rotated on
                         2026-08-20; each cluster has its own. Read it with:
                           kubectl --context <ctx> get secret nakama -n rpg-k8s-data \
                             -o jsonpath='{.data.NAKAMA_SERVER_KEY}' | base64 -d
  --map ID               Default map_01
  --status-url URL       Game server /status. Required for the players_online row.
  --kube-context CTX     Enables the Redis rows. Omitted = those rows report SKIP.
  --redis-ns NS          Default rpg-k8s-data
  --redis-sts NAME       Default redis
  --shots DIR            Where to write window captures (default: a temp dir).
  --settle SECONDS       Wait before asserting (default 45). Joins are not instant.
  --keep                 Leave the players running afterwards.

Exit code is 0 only when every ASSERTED row passed. Rows that could not run are
reported as NOT CHECKED and do not fail the run, but they are never silent.
USAGE
}

while [ $# -gt 0 ]; do
    case "$1" in
        --exe) EXE="$2"; shift 2 ;;
        --count) COUNT="$2"; shift 2 ;;
        --gateway-host) GATEWAY_HOST="$2"; shift 2 ;;
        --gateway-port) GATEWAY_PORT="$2"; shift 2 ;;
        --nakama-host) NAKAMA_HOST="$2"; shift 2 ;;
        --nakama-port) NAKAMA_PORT="$2"; shift 2 ;;
        --nakama-key) NAKAMA_KEY="$2"; shift 2 ;;
        --map) MAP_ID="$2"; shift 2 ;;
        --status-url) STATUS_URL="$2"; shift 2 ;;
        --kube-context) KUBE_CONTEXT="$2"; shift 2 ;;
        --redis-ns) REDIS_NS="$2"; shift 2 ;;
        --redis-sts) REDIS_STS="$2"; shift 2 ;;
        --shots) SHOT_DIR="$2"; shift 2 ;;
        --settle) SETTLE="$2"; shift 2 ;;
        --keep) KEEP=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

for required in EXE GATEWAY_PORT NAKAMA_PORT NAKAMA_KEY; do
    if [ -z "${!required}" ]; then
        echo "missing --$(echo "$required" | tr '[:upper:]_' '[:lower:]-')" >&2
        usage >&2
        exit 2
    fi
done

PASS=0
FAIL=0
SKIP=0

pass() { printf '  PASS        %s\n' "$1"; PASS=$((PASS + 1)); }
fail() { printf '  FAIL        %s\n' "$1"; printf '              expected: %s\n              actual:   %s\n' "$2" "$3"; FAIL=$((FAIL + 1)); }
skip() { printf '  NOT CHECKED %s\n              %s\n' "$1" "$2"; SKIP=$((SKIP + 1)); }

TAG="verify-$(date +%Y%m%d-%H%M%S)"
LOG_DIR="${TMPDIR:-/tmp}/cuvara-verify-$TAG"
mkdir -p "$LOG_DIR"
[ -n "$SHOT_DIR" ] || SHOT_DIR="$LOG_DIR/shots"
mkdir -p "$SHOT_DIR"

echo "verify-multiclient  count=$COUNT map=$MAP_ID"
echo "  gateway  $GATEWAY_HOST:$GATEWAY_PORT"
echo "  nakama   $NAKAMA_HOST:$NAKAMA_PORT"
echo "  logs     $LOG_DIR"
echo

cleanup() {
    if [ "$KEEP" -eq 0 ]; then
        "$HERE/run-clients.sh" --exe "$EXE" --kill >/dev/null 2>&1 || true
    fi
}
trap cleanup EXIT

# A player left running from an earlier attempt holds lib_burst_generated.dll and
# also pollutes players_online, so start from a known-empty state rather than
# assuming one.
"$HERE/run-clients.sh" --exe "$EXE" --kill >/dev/null 2>&1 || true

"$HERE/run-clients.sh" \
    --exe "$EXE" --count "$COUNT" --tag "$TAG" --log-dir "$LOG_DIR" \
    --gateway-host "$GATEWAY_HOST" --gateway-port "$GATEWAY_PORT" \
    --nakama-host "$NAKAMA_HOST" --nakama-port "$NAKAMA_PORT" \
    --nakama-key "$NAKAMA_KEY" --map "$MAP_ID" \
    ${STATUS_URL:+--status-url "$STATUS_URL"} \
    --tile >/dev/null

echo "launched, settling for ${SETTLE}s"
sleep "$SETTLE"
echo

LOGS=("$LOG_DIR/$TAG"-*.log)

# --- IN WORLD -------------------------------------------------------------
in_world=0
for f in "${LOGS[@]}"; do
    [ -f "$f" ] && grep -q "IN WORLD" "$f" && in_world=$((in_world + 1))
done
if [ "$in_world" -eq "$COUNT" ]; then
    pass "all $COUNT clients reached IN WORLD"
else
    fail "every client reaches IN WORLD" "$COUNT" "$in_world"
fi

# --- distinct user ids ----------------------------------------------------
# Shared identity is the failure this hunts: two instances authenticating as one
# Nakama user evict each other, and the survivor sits alone looking exactly like
# a broken area-of-interest.
ids=$(awk '/Auth OK/{for (i = 1; i <= NF; i++) if ($i ~ /user_id=/) print $i}' "${LOGS[@]}" 2>/dev/null | sort -u | wc -l)
if [ "$ids" -eq "$COUNT" ]; then
    pass "$COUNT distinct Nakama user ids"
else
    fail "$COUNT distinct user ids" "$COUNT" "$ids"
fi

# --- one server address for everyone (ADR-2) ------------------------------
addrs=$(awk '/assigned to/{for (i = 1; i <= NF; i++) if ($i ~ /^[0-9].*:[0-9]+$/) print $i}' "${LOGS[@]}" 2>/dev/null | sort -u)
addr_count=$(printf '%s\n' "$addrs" | grep -c . || true)
if [ "$addr_count" -eq 1 ]; then
    pass "every client assigned the same game server ($(printf '%s' "$addrs"))"
elif [ "$addr_count" -eq 0 ]; then
    skip "one game server address for all clients" "no assignment line matched in the logs; the log format may have changed"
else
    fail "one game server address (ADR-2: one live server per map)" "1 distinct address" "$addr_count: $(printf '%s' "$addrs" | tr '\n' ' ')"
fi

# --- no fatal -------------------------------------------------------------
fatal=$(awk '/FATAL|Unhandled exception|UnityWebRequestException/{print FILENAME": "substr($0, 1, 120)}' "${LOGS[@]}" 2>/dev/null | head -3)
if [ -z "$fatal" ]; then
    pass "no FATAL or unhandled exception in any client log"
else
    fail "no FATAL in any client log" "none" "$fatal"
fi

# --- players_online -------------------------------------------------------
if [ -n "$STATUS_URL" ]; then
    # Three outcomes, not two, and they must not be conflated. An unreachable
    # endpoint is NOT a wrong player count -- reporting it as "expected 3, actual
    # ?" accuses the server of a fault that belongs to the harness. This bit on
    # the first real run: a rescheduled Agones pod left the port-forward pointing
    # at a dead pod, and the row failed while all three players were in fact
    # online. Same lesson as #216 on the server side: a bare null is the defect
    # underneath the defect.
    status_body=$(curl -s -m 10 --fail-with-body "$STATUS_URL" 2>/dev/null)
    curl_rc=$?
    if [ "$curl_rc" -ne 0 ] || [ -z "$status_body" ]; then
        skip "game server players_online" \
            "could not reach $STATUS_URL (curl exit $curl_rc). This is the harness, not the server -- the endpoint is usually a kubectl port-forward, and an Agones pod that was rescheduled leaves it pointing at a pod that no longer exists. Re-establish it against the CURRENT pod and re-run. The Redis session count above already indicates whether the clients joined."
    else
        online=$(printf '%s' "$status_body" | python3 -c 'import sys,json;print(json.load(sys.stdin).get("players_online","<absent>"))' 2>/dev/null || echo "<unparseable>")
        if [ "$online" = "$COUNT" ]; then
            pass "game server reports players_online=$COUNT"
        else
            fail "players_online=$COUNT" "$COUNT" "$online (from $STATUS_URL)"
        fi
    fi
else
    skip "game server players_online" "--status-url not given"
fi

# --- redis ----------------------------------------------------------------
if [ -n "$KUBE_CONTEXT" ]; then
    redis() { kubectl --context "$KUBE_CONTEXT" exec -n "$REDIS_NS" "statefulset/$REDIS_STS" -- redis-cli "$@" 2>/dev/null; }

    sessions=$(redis --scan --pattern 'session:*' | grep -c . || true)
    if [ "$sessions" -eq "$COUNT" ]; then
        pass "Redis holds $COUNT session keys"
    else
        fail "$COUNT session keys" "$COUNT" "$sessions"
    fi

    # The raw key COUNT is not the number to read here: the registry writes both
    # a servers:map:<id> index and a servers:id:<server> entry, so `KEYS servers:*`
    # is two for a healthy single-server map. The meaningful figure is the map
    # set's cardinality -- more than one member means the clients could have
    # landed on different servers and would never see each other.
    members=$(redis smembers "servers:map:$MAP_ID" | grep -c . || true)
    if [ "$members" -eq 1 ]; then
        pass "servers:map:$MAP_ID holds exactly one server (ADR-2)"
    else
        fail "one server registered for $MAP_ID (ADR-2)" "1 member" "$members"
    fi
else
    skip "Redis session and registry rows" "--kube-context not given"
fi

# --- window capture -------------------------------------------------------
PIDS=$(powershell.exe -NoProfile -Command \
    "(Get-Process IndieRPGMMOAdventure -EA SilentlyContinue).Id -join ','" 2>/dev/null | tr -d '\r')

if [ -n "$PIDS" ]; then
    SHOT_WIN=$(wslpath -w "$SHOT_DIR" 2>/dev/null || echo "$SHOT_DIR")
    PS1_FILE="$LOG_DIR/capture.ps1"
    cat > "$PS1_FILE" <<'PSCRIPT'
param([int[]]$Pids, [string]$OutDir)
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;using System.Runtime.InteropServices;
public class P {
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint f);
 [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr h, out R r);
 [StructLayout(LayoutKind.Sequential)] public struct R { public int L,T,Rt,B; }
}
"@
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
foreach ($p in $Pids) {
  $proc = Get-Process -Id $p -ErrorAction SilentlyContinue
  if (-not $proc -or $proc.MainWindowHandle -eq 0) { Write-Output "$p no-window"; continue }
  $r = New-Object P+R
  [void][P]::GetClientRect($proc.MainWindowHandle, [ref]$r)
  if ($r.Rt -le 0 -or $r.B -le 0) { Write-Output "$p bad-rect"; continue }
  $bmp = New-Object System.Drawing.Bitmap $r.Rt, $r.B
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $hdc = $g.GetHdc()
  # 2 = PW_RENDERFULLCONTENT: reads the window's own content while it stays
  # occluded. Raising the window instead does not work from a background
  # process and silently captures whatever is on top.
  [void][P]::PrintWindow($proc.MainWindowHandle, $hdc, 2)
  $g.ReleaseHdc($hdc)
  $bmp.Save((Join-Path $OutDir "client-$p.png"), [System.Drawing.Imaging.ImageFormat]::Png)
  $g.Dispose(); $bmp.Dispose()
  Write-Output "$p captured"
}
PSCRIPT
    PS1_WIN=$(wslpath -w "$PS1_FILE" 2>/dev/null || echo "$PS1_FILE")
    powershell.exe -NoProfile -ExecutionPolicy Bypass -Command \
        "& '$PS1_WIN' -Pids @($PIDS) -OutDir '$SHOT_WIN'" >/dev/null 2>&1
    shots=$(find "$SHOT_DIR" -name 'client-*.png' 2>/dev/null | wc -l)
    skip "each window holds every player (MUTUAL VISIBILITY)" \
        "$shots screenshot(s) in $SHOT_DIR. This is the only row that proves clients can see EACH OTHER, and it needs a human -- $COUNT clients that each see only themselves pass every other row above.

              Read the HUD's 'Entities:' count, NOT the number of capsules. Players with no
              input all stand on the spawn point, so their capsules and name labels stack and
              only the one drawn last is legible -- a window can look like it holds one player
              while its world state holds all of them. Measured: with $COUNT clients idle and 6
              enemies alive, every window read 'Entities: 9', and the server's own player_states
              rows confirmed all $COUNT were at (0,0). Counting capsules there would have
              reported a fault that did not exist.

              So: Entities should equal $COUNT + the enemy count in the Server Status panel, on
              EVERY window. Also check 'Predict ... err' is near zero. To judge the capsules
              visually instead, first move a client so the players separate."
else
    skip "each window shows $COUNT capsules" "no player window found to capture"
fi

echo
echo "  $PASS passed, $FAIL failed, $SKIP not checked"
[ "$KEEP" -eq 1 ] && echo "  players left running (--keep); stop with: $HERE/run-clients.sh --exe \"$EXE\" --kill"
echo
[ "$FAIL" -eq 0 ] || exit 1
exit 0
