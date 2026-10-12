#!/bin/sh
# Shinshi game server cooperative stop (systemd ExecStop helper).
#
# usage: cooperative-stop.sh <main-pid> <request-file> [max-wait-seconds]
#
# Writes the main PID into the request file inside the service-private RuntimeDirectory,
# then waits (bounded) for the server process to exit by itself. The server drains new
# assignments, waits for in-progress sorties and exits. If the process is still alive after
# the wait, this script returns and systemd falls back to SIGTERM / SIGKILL.
# No secrets are read or printed.

set -u

PID="${1:-}"
FILE="${2:-}"
MAX_WAIT="${3:-270}"

case "$PID" in
    ''|*[!0-9]*|0)
        # main process is not running, nothing to wait for
        exit 0
        ;;
esac

case "$FILE" in
    /*) ;;
    *)
        echo "cooperative-stop: request file path must be absolute" >&2
        exit 1
        ;;
esac

case "$MAX_WAIT" in
    ''|*[!0-9]*)
        echo "cooperative-stop: max wait must be an integer" >&2
        exit 1
        ;;
esac

if ! kill -0 "$PID" 2>/dev/null; then
    exit 0
fi

TMP="${FILE}.tmp.$$"
if ! ( umask 077 && printf '%s\n' "$PID" > "$TMP" ) 2>/dev/null; then
    echo "cooperative-stop: cannot write request file, falling back to SIGTERM" >&2
    rm -f "$TMP" 2>/dev/null
    exit 1
fi

if ! mv -f "$TMP" "$FILE" 2>/dev/null; then
    echo "cooperative-stop: cannot publish request file, falling back to SIGTERM" >&2
    rm -f "$TMP" 2>/dev/null
    exit 1
fi

waited=0
while kill -0 "$PID" 2>/dev/null; do
    if [ "$waited" -ge "$MAX_WAIT" ]; then
        echo "cooperative-stop: server still running after ${MAX_WAIT}s, systemd will terminate it" >&2
        exit 0
    fi

    sleep 1
    waited=$((waited + 1))
done

exit 0
