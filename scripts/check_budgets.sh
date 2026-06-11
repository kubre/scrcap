#!/bin/bash
# Size budget gate (plan §08): a failing budget blocks merge like a failing
# test. Binary budget: < 5 MB (stretch 3 MB).
set -euo pipefail

APP="${1:-dist/scrcap.app}"
BUDGET_BYTES=$((5 * 1024 * 1024))
STRETCH_BYTES=$((3 * 1024 * 1024))

if [ ! -d "$APP" ]; then
    echo "✗ $APP not found — run scripts/make_app.sh first" >&2
    exit 1
fi

SIZE=$(du -sk "$APP" | cut -f1)
SIZE_BYTES=$((SIZE * 1024))
HUMAN=$(du -sh "$APP" | cut -f1)

if [ "$SIZE_BYTES" -gt "$BUDGET_BYTES" ]; then
    echo "✗ budget FAIL: $APP is $HUMAN (budget: 5 MB)" >&2
    exit 1
elif [ "$SIZE_BYTES" -gt "$STRETCH_BYTES" ]; then
    echo "✓ budget OK: $APP is $HUMAN (< 5 MB; stretch 3 MB missed)"
else
    echo "✓ budget OK: $APP is $HUMAN (under 3 MB stretch goal)"
fi
