#!/usr/bin/env bash
set -euo pipefail

set +e
GDK_BACKEND=x11 xvfb-run -a timeout 20s "$@"
status=$?
set -e

if [ "$status" -ne 124 ]; then
    echo "Linux preview exited during the startup check (status $status)." >&2
    exit 1
fi

echo "Linux preview remained running for 20 seconds under Xvfb."
