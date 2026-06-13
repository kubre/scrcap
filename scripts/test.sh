#!/bin/bash
set -euo pipefail

cd "$(dirname "$0")/.."

swift build
swift run scrcap-core-tests

SCRCAP_SMOKE=text .build/debug/scrcap
SCRCAP_SMOKE=ui .build/debug/scrcap
