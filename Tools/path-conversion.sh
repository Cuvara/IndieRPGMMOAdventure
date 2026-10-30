#!/usr/bin/env bash
# Shared path conversion for Windows tools launched from WSL or Git Bash.

towin() {
    if command -v wslpath >/dev/null 2>&1; then
        wslpath -w "$1"
    elif command -v cygpath >/dev/null 2>&1; then
        cygpath -w "$1"
    else
        echo "error: converting paths for Windows requires wslpath or cygpath" >&2
        return 1
    fi
}

if [ "${BASH_SOURCE[0]}" = "$0" ]; then
    if [ "${1:-}" != "--self-test" ]; then
        echo "Usage: path-conversion.sh --self-test" >&2
        exit 2
    fi
    converted="$(towin "$PWD")" || exit 1
    case "$converted" in
        *:*|'\'*) printf 'PASS path conversion: %s -> %s\n' "$PWD" "$converted" ;;
        *) echo "error: path conversion did not produce a Windows drive or UNC path: $converted" >&2; exit 1 ;;
    esac
fi
