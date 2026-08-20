#!/usr/bin/env bash
#
# Launch N copies of the Windows player against one backend, each as a distinct
# player.
#
# Why this exists: multiplayer behaviour cannot be proved by one client. Three
# processes on one map, each authenticating as its own Nakama user, is the
# smallest arrangement in which "A sees B" is a real observation.
#
# The three things that have to differ per instance, and what would happen if
# they did not:
#   * device id   -> two instances would authenticate as the SAME Nakama user,
#                    the second login would evict the first, and the survivor
#                    would sit alone in the world. That reads exactly like a
#                    broken area-of-interest: a failure disguised as a pass.
#   * log file    -> Unity players write to one shared Player.log per product,
#                    so three instances interleave into an unreadable file and
#                    the evidence is gone.
#   * window      -> three windows stacked on the same pixels cannot be compared.
#
# Everything about the backend is a parameter. The game server is an Agones pod
# whose port is assigned at scheduling time, so nothing here may be hardcoded.
#
# Requires: the player built by Assets/BuildScripts/Editor/PlayerBuilder.cs.
# Run from WSL or Git Bash; the .exe is launched through the Windows loader.

set -euo pipefail

EXE=""
COUNT=3
GATEWAY_HOST="127.0.0.1"
GATEWAY_PORT="8000"
NAKAMA_HOST="127.0.0.1"
NAKAMA_PORT="7350"
NAKAMA_SCHEME="http"
NAKAMA_KEY="defaultkey"
MAP_ID="map_01"
STATUS_URL=""
LOG_DIR=""
WIDTH=800
HEIGHT=600
TAG="mc"
TILE=0
DO_KILL=0

usage() {
    cat <<'USAGE'
Usage: run-clients.sh --exe <player.exe> [options]

  --exe PATH            Built Windows player (WSL or Windows path). Required
                        unless --kill is the only action.
  --count N             Instances to start (default 3).
  --gateway-host HOST   Gateway host (default 127.0.0.1).
  --gateway-port PORT   Gateway port (default 8000).
  --nakama-scheme S     http or https (default http).
  --nakama-host HOST    Nakama host (default 127.0.0.1).
  --nakama-port PORT    Nakama HTTP port (default 7350).
  --nakama-key KEY      Nakama server key (default defaultkey).
  --map ID              Map to join (default map_01). Passing it also collapses
                        the in-game map selector, which otherwise waits for a
                        click and would leave every window at a menu.
  --status-url URL      Game server /status endpoint for the HUD panel, e.g.
                        http://127.0.0.1:9101/status. Agones assigns this port
                        at scheduling time, so it has no useful default.
  --log-dir DIR         Where per-instance logs go (default /tmp/cuvara-clients).
  --width N/--height N  Window size (default 800x600).
  --tag NAME            Prefix for device ids and log names (default mc).
  --tile                Best-effort: arrange the windows side by side (PowerShell).
  --kill                Kill every running instance of the player and exit.
                        Do this before any rebuild: a running player holds
                        lib_burst_generated.dll open and the build fails on it.
  -h, --help            This text.

Example — three clients against the local dev stack:
  Tools/run-clients.sh --exe Builds/MultiClient/StandaloneWindows64/IndieRPGMMOAdventure.exe \
      --count 3 --gateway-host 127.0.0.1 --gateway-port 8000 \
      --nakama-host 127.0.0.1 --nakama-port 7350 \
      --map map_01 --status-url http://127.0.0.1:9101/status --tile
USAGE
}

while [ $# -gt 0 ]; do
    case "$1" in
        --exe) EXE="$2"; shift 2 ;;
        --count) COUNT="$2"; shift 2 ;;
        --gateway-host) GATEWAY_HOST="$2"; shift 2 ;;
        --gateway-port) GATEWAY_PORT="$2"; shift 2 ;;
        --nakama-scheme) NAKAMA_SCHEME="$2"; shift 2 ;;
        --nakama-host) NAKAMA_HOST="$2"; shift 2 ;;
        --nakama-port) NAKAMA_PORT="$2"; shift 2 ;;
        --nakama-key) NAKAMA_KEY="$2"; shift 2 ;;
        --map) MAP_ID="$2"; shift 2 ;;
        --status-url) STATUS_URL="$2"; shift 2 ;;
        --log-dir) LOG_DIR="$2"; shift 2 ;;
        --width) WIDTH="$2"; shift 2 ;;
        --height) HEIGHT="$2"; shift 2 ;;
        --tag) TAG="$2"; shift 2 ;;
        --tile) TILE=1; shift ;;
        --kill) DO_KILL=1; shift ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if [ "$DO_KILL" = "1" ]; then
    # /IM matches by image name, so this catches every instance regardless of how
    # it was started. A missing process is not an error here.
    taskkill.exe /F /IM "$(basename "${EXE:-IndieRPGMMOAdventure.exe}")" >/dev/null 2>&1 || true
    echo "killed running players"
    exit 0
fi

if [ -z "$EXE" ]; then
    echo "error: --exe is required" >&2
    usage >&2
    exit 2
fi

if ! [ "$COUNT" -ge 1 ] 2>/dev/null; then
    echo "error: --count must be a positive integer" >&2
    exit 2
fi

# Accept either a WSL path or a Windows path for the exe.
if [ ! -f "$EXE" ]; then
    if command -v wslpath >/dev/null 2>&1; then
        CANDIDATE="$(wslpath -u "$EXE" 2>/dev/null || true)"
        if [ -n "$CANDIDATE" ] && [ -f "$CANDIDATE" ]; then
            EXE="$CANDIDATE"
        fi
    fi
fi

if [ ! -f "$EXE" ]; then
    echo "error: player not found: $EXE" >&2
    echo "       build it first, see Tools/run-clients.sh header and CLAUDE.md" >&2
    exit 1
fi

LOG_DIR="${LOG_DIR:-/tmp/cuvara-clients}"
mkdir -p "$LOG_DIR"

towin() {
    if command -v wslpath >/dev/null 2>&1; then
        wslpath -w "$1"
    else
        printf '%s\n' "$1"
    fi
}

# One stamp for the whole launch, so every instance of one run shares a prefix
# and the per-instance index is what tells them apart in the server logs.
RUN_STAMP="$(date +%Y%m%d-%H%M%S)"

echo "backend  gateway=${GATEWAY_HOST}:${GATEWAY_PORT} nakama=${NAKAMA_SCHEME}://${NAKAMA_HOST}:${NAKAMA_PORT} map=${MAP_ID}"
echo "player   $EXE"
echo "logs     $LOG_DIR"
echo

PIDS=()
for i in $(seq 1 "$COUNT"); do
    DEVICE="${TAG}-${RUN_STAMP}-${i}"
    LOG="${LOG_DIR}/${TAG}-${RUN_STAMP}-${i}.log"

    ARGS=(
        -logFile "$(towin "$LOG")"
        -screen-fullscreen 0
        -screen-width "$WIDTH"
        -screen-height "$HEIGHT"
        -popupwindow
        -cuvara-gateway-host "$GATEWAY_HOST"
        -cuvara-gateway-port "$GATEWAY_PORT"
        -cuvara-nakama-scheme "$NAKAMA_SCHEME"
        -cuvara-nakama-host "$NAKAMA_HOST"
        -cuvara-nakama-port "$NAKAMA_PORT"
        -cuvara-nakama-key "$NAKAMA_KEY"
        -cuvara-map "$MAP_ID"
        -cuvara-device "$DEVICE"
        -cuvara-instance "$i"
    )

    if [ -n "$STATUS_URL" ]; then
        ARGS+=(-cuvara-status-url "$STATUS_URL")
    fi

    "$EXE" "${ARGS[@]}" >/dev/null 2>&1 &
    PIDS+=("$!")
    echo "instance $i  device=$DEVICE  log=$LOG"

    # Stagger the starts. Three simultaneous device authentications against a
    # cold Nakama have raced badly enough to fail one of them, and a client that
    # never authenticated is easy to mistake for a client that cannot see peers.
    sleep 2
done

echo
echo "started ${#PIDS[@]} instance(s): ${PIDS[*]}"

if [ "$TILE" = "1" ]; then
    # Best effort only: window placement is not something the run depends on.
    powershell.exe -NoProfile -Command "
        Add-Type -Namespace W -Name U -MemberDefinition '
            [DllImport(\"user32.dll\")] public static extern bool MoveWindow(IntPtr h,int x,int y,int w,int t,bool r);';
        \$i=0;
        Get-Process -Name '$(basename "$EXE" .exe)' -ErrorAction SilentlyContinue | ForEach-Object {
            if (\$_.MainWindowHandle -ne 0) {
                [W.U]::MoveWindow(\$_.MainWindowHandle, \$i*$((WIDTH+10)), 0, $WIDTH, $HEIGHT, \$true) | Out-Null;
                \$i++
            }
        }" >/dev/null 2>&1 || echo "tile: skipped (PowerShell unavailable)"
fi

cat <<EOF

Watch:  tail -f ${LOG_DIR}/${TAG}-${RUN_STAMP}-*.log
Stop:   Tools/run-clients.sh --exe "$EXE" --kill
EOF
