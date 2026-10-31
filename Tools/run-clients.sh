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
# Process safety: every player this script starts is recorded (Windows PID plus exe
# path) in <log-dir>/clients.pids, and --kill stops only those PIDs. It never kills
# by image name, so players the user or another run started are left alone.
#
# Requires: the player built by Assets/BuildScripts/Editor/PlayerBuilder.cs.
# Run from WSL or Git Bash; the .exe is launched through the Windows loader.

set -euo pipefail
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
source "$SCRIPT_DIR/path-conversion.sh"

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
DO_SELF_TEST=0
PID_FILE_NAME="clients.pids"
EXTRA_ARGS=()
# KCP datagram key for the gameplay hop (the game server's TRANSPORT_KEY). A Windows player
# started from WSL does not inherit WSL's environment, so it is forwarded as a flag.
TRANSPORT_KEY_ARG="${CUVARA_TRANSPORT_KEY:-${TRANSPORT_KEY:-}}"

# ---- pid-scoped process control ----------------------------------------------
# All of these act on explicit Windows PIDs. Nothing here matches by image name
# for the purpose of stopping a process.

# Run a PowerShell snippet, stdout only, CRs stripped.
ps() { powershell.exe -NoProfile -NonInteractive -Command "$1" 2>/dev/null | tr -d '\r'; }

# Live image path of Windows PID $1; empty when the PID is not running.
image_of_pid() {
    ps "(Get-Process -Id $1 -ErrorAction SilentlyContinue).Path"
}

# Windows paths compare case-insensitively and may use either slash.
same_image() {
    local a b
    a="$(printf '%s' "$1" | tr 'A-Z/' 'a-z\\')"
    b="$(printf '%s' "$2" | tr 'A-Z/' 'a-z\\')"
    [ -n "$a" ] && [ "$a" = "$b" ]
}

# Start Windows exe $1 with the remaining args, detached; print its WINDOWS pid.
# Git Bash: $! is an MSYS pid, /proc/<pid>/winpid maps it. WSL: $! is an interop
# pid, so launch through Start-Process -PassThru and read .Id.
launch_win() {
    local exe="$1"; shift
    if [ -r "/proc/$$/winpid" ]; then
        "$exe" "$@" >/dev/null 2>&1 &
        local mpid=$!
        local wpid
        wpid="$(cat "/proc/$mpid/winpid" 2>/dev/null || true)"
        printf '%s\n' "${wpid:-$mpid}"
    else
        local list="" a winexe
        for a in "$@"; do
            case "$a" in *[[:space:]]*) a="\"$a\"" ;; esac
            list+="'${a//\'/\'\'}',"
        done
        list="${list%,}"
        winexe="$(towin "$exe")" || return 1
        if [ -n "$list" ]; then
            ps "(Start-Process -FilePath '${winexe//\'/\'\'}' -ArgumentList @($list) -PassThru).Id"
        else
            ps "(Start-Process -FilePath '${winexe//\'/\'\'}' -PassThru).Id"
        fi
    fi
}

# Append "pid<TAB>winexe" to pid file $1.
record_pid() { printf '%s\t%s\n' "$2" "$3" >> "$1"; }

# Stop one recorded player: only if the live image path still matches.
# Prints a result line; returns 0 if stopped or already gone, 1 if left alone.
stop_recorded() {
    local pid="$1" exe="$2" live
    case "$pid" in ''|*[!0-9]*) echo "skip: bad pid '$pid'"; return 1 ;; esac
    live="$(image_of_pid "$pid")"
    if [ -z "$live" ]; then
        echo "gone: pid $pid already exited"
        return 0
    fi
    if ! same_image "$live" "$exe"; then
        echo "skip: pid $pid now runs '$live', not '$exe' (pid reused); left alone"
        return 1
    fi
    MSYS2_ARG_CONV_EXCL='*' MSYS_NO_PATHCONV=1 taskkill.exe /PID "$pid" /T /F >/dev/null 2>&1 || true
    echo "stopped: pid $pid ($exe)"
    return 0
}

# --kill: act on the pid file in directory $1 only.
kill_recorded() {
    local dir="$1" file="$1/$PID_FILE_NAME" pid exe rc=0
    if [ ! -f "$file" ]; then
        echo "no pid file at $file: nothing to stop (nothing was killed)."
        echo "Players currently running (read-only listing):"
        ps "Get-Process -Name '$(basename "${EXE:-IndieRPGMMOAdventure.exe}" .exe)' -ErrorAction SilentlyContinue | ForEach-Object { '  pid ' + \$_.Id + '  ' + \$_.Path }"
        echo "Stop the ones you own by PID: taskkill /PID <pid> /T /F"
        return 0
    fi
    while IFS=$'\t' read -r pid exe; do
        [ -n "$pid" ] || continue
        stop_recorded "$pid" "$exe" || rc=1
    done < "$file"
    rm -f "$file"
    return "$rc"
}

self_test() {
    local dir ok=1 pid live ping_win ping_unix out
    dir="$(mktemp -d)"
    t() { if [ "$1" = ok ]; then printf 'PASS %s\n' "$2"; else printf 'FAIL %s\n' "$2"; ok=0; fi; }
    alive() { [ -n "$(image_of_pid "$1")" ]; }

    # Harmless child: ping -n 60 to loopback, started by this test.
    ping_win="$(ps "(Get-Command ping.exe).Source")"
    if command -v wslpath >/dev/null 2>&1; then ping_unix="$(wslpath -u "$ping_win")"
    else ping_unix="$(cygpath -u "$ping_win")"; fi
    [ -x "$ping_unix" ] || { echo "FAIL cannot locate ping.exe"; rm -rf "$dir"; return 1; }
    pid="$(launch_win "$ping_unix" -n 60 127.0.0.1)"
    case "$pid" in ''|*[!0-9]*) echo "FAIL could not start child (got '$pid')"; rm -rf "$dir"; return 1 ;; esac
    sleep 1
    live="$(image_of_pid "$pid")"
    if [ -n "$live" ]; then t ok "child started, windows pid $pid, image $live"; else t no "child not running"; fi

    # 1. no pid file -> no kill
    out="$(kill_recorded "$dir")"
    if alive "$pid" && printf '%s' "$out" | grep -q 'nothing was killed'; then t ok "no pid file: nothing killed"; else t no "no pid file killed or misreported: $out"; fi

    # 2. pid file write/read round trip
    record_pid "$dir/$PID_FILE_NAME" "$pid" "$live"
    if [ "$(cut -f1 "$dir/$PID_FILE_NAME")" = "$pid" ] && [ "$(cut -f2 "$dir/$PID_FILE_NAME")" = "$live" ]; then t ok "pid file round trip"; else t no "pid file content wrong"; fi

    # 3. image mismatch (PID reuse) -> left alone
    rm -f "$dir/$PID_FILE_NAME"
    record_pid "$dir/$PID_FILE_NAME" "$pid" 'C:\Nope\Other.exe'
    kill_recorded "$dir" >/dev/null || true
    if alive "$pid"; then t ok "image mismatch: process left alone"; else t no "image mismatch still killed pid $pid"; ok=0; fi

    # 4. matching image -> stopped
    record_pid "$dir/$PID_FILE_NAME" "$pid" "$live"
    kill_recorded "$dir" >/dev/null || true
    sleep 1
    if ! alive "$pid"; then t ok "matching image: recorded pid stopped"; else t no "recorded pid still running"; fi
    if [ ! -f "$dir/$PID_FILE_NAME" ]; then t ok "pid file removed after kill"; else t no "pid file left behind"; fi

    # 5. already-exited pid is not an error
    record_pid "$dir/$PID_FILE_NAME" "$pid" "$live"
    if kill_recorded "$dir" >/dev/null; then t ok "exited pid handled"; else t no "exited pid reported failure"; fi

    # Safety net: stop only the child this test started, if still alive.
    if alive "$pid"; then MSYS2_ARG_CONV_EXCL='*' taskkill.exe /PID "$pid" /T /F >/dev/null 2>&1 || true; fi
    rm -rf "$dir"
    if [ "$ok" = 1 ]; then echo "self-test: all passed"; else echo "self-test: FAILED"; return 1; fi
}

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
  --transport-key HEX   KCP datagram key for the gameplay hop (64 hex chars, the
                        game server's TRANSPORT_KEY); forwarded as
                        -cuvara-transport-key. Defaults to $CUVARA_TRANSPORT_KEY,
                        then $TRANSPORT_KEY; empty = plaintext datagrams (dev).
                        Gameplay is KCP/UDP only: the game-server port must be
                        open for UDP.
  --log-dir DIR         Where per-instance logs and clients.pids go
                        (default /tmp/cuvara-clients).
  --width N/--height N  Window size (default 800x600).
  --tag NAME            Prefix for device ids and log names (default mc).
  --tile                Best-effort: arrange the windows side by side (PowerShell).
  --kill                Stop the players THIS script started (PIDs recorded in
                        <log-dir>/clients.pids, one "pid<TAB>exe" line each) and
                        exit. A PID is stopped only while its live image path
                        still equals the recorded exe (guards against PID reuse),
                        with taskkill /PID <pid> /T /F. Players it did not start
                        are never touched. Without a pid file it only lists the
                        running players and tells you to stop them by PID.
                        Pass the same --log-dir as the launch. Do this before any
                        rebuild: a running player holds lib_burst_generated.dll
                        open and the build fails on it.
  --self-test           Exercise the pid-file / image-check / kill logic against
                        a harmless child process it starts itself, then exit.
  -- ARGS...            Everything after -- is passed to every player instance
                        verbatim, e.g. -- -bench -bench-duration 60 (see
                        docs/DEVICE-BENCHMARK.md).
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
        --transport-key) TRANSPORT_KEY_ARG="$2"; shift 2 ;;
        --log-dir) LOG_DIR="$2"; shift 2 ;;
        --width) WIDTH="$2"; shift 2 ;;
        --height) HEIGHT="$2"; shift 2 ;;
        --tag) TAG="$2"; shift 2 ;;
        --tile) TILE=1; shift ;;
        --kill) DO_KILL=1; shift ;;
        --self-test) DO_SELF_TEST=1; shift ;;
        # Everything after -- goes to every player instance verbatim — the device
        # benchmark's -bench flags (docs/DEVICE-BENCHMARK.md) are the consumer.
        --) shift; EXTRA_ARGS=("$@"); break ;;
        -h|--help) usage; exit 0 ;;
        *) echo "unknown option: $1" >&2; usage >&2; exit 2 ;;
    esac
done

if [ "$DO_SELF_TEST" = "1" ]; then
    self_test
    exit $?
fi

if [ "$DO_KILL" = "1" ]; then
    kill_recorded "${LOG_DIR:-/tmp/cuvara-clients}"
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

# One stamp for the whole launch, so every instance of one run shares a prefix
# and the per-instance index is what tells them apart in the server logs.
RUN_STAMP="$(date +%Y%m%d-%H%M%S)"

echo "backend  gateway=${GATEWAY_HOST}:${GATEWAY_PORT} nakama=${NAKAMA_SCHEME}://${NAKAMA_HOST}:${NAKAMA_PORT} map=${MAP_ID}"
echo "gameplay KCP/UDP, transport key $([ -n "$TRANSPORT_KEY_ARG" ] && echo set || echo 'not set (plaintext)')"
echo "player   $EXE"
echo "logs     $LOG_DIR"
echo

PID_FILE="$LOG_DIR/$PID_FILE_NAME"
# Recorded with the Windows path: --kill compares it with the live image path.
WINEXE="$(towin "$(cd "$(dirname "$EXE")" && pwd)/$(basename "$EXE")")"
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

    if [ -n "$TRANSPORT_KEY_ARG" ]; then
        ARGS+=(-cuvara-transport-key "$TRANSPORT_KEY_ARG")
    fi

    if [ "${#EXTRA_ARGS[@]}" -gt 0 ]; then
        ARGS+=("${EXTRA_ARGS[@]}")
    fi

    WPID="$(launch_win "$EXE" "${ARGS[@]}")"
    case "$WPID" in ''|*[!0-9]*) echo "error: could not start instance $i (no windows pid: '$WPID')" >&2; exit 1 ;; esac
    record_pid "$PID_FILE" "$WPID" "$WINEXE"
    PIDS+=("$WPID")
    echo "instance $i  device=$DEVICE  log=$LOG"

    # Stagger the starts. Three simultaneous device authentications against a
    # cold Nakama have raced badly enough to fail one of them, and a client that
    # never authenticated is easy to mistake for a client that cannot see peers.
    sleep 2
done

echo
echo "started ${#PIDS[@]} instance(s), windows pids: ${PIDS[*]}"
echo "pid file $PID_FILE"

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
Stop:   Tools/run-clients.sh --exe "$EXE" --log-dir "$LOG_DIR" --kill
EOF
