#!/usr/bin/env bash
set -euo pipefail

repo=$(git rev-parse --show-toplevel)
export ULTRASIM_BOT_CHECKPOINT=PF4F
export ULTRASIM_PUBLIC_REPRESENTATION=message129
export ULTRASIM_PUBLIC_CADENCE=1/2/4
export ULTRASIM_PUBLIC_COMPRESSION=${ULTRASIM_PUBLIC_COMPRESSION:-lz4}
export ULTRASIM_UNRELIABLE_FRAME_CAP=2
export ULTRASIM_UNRELIABLE_AGGREGATION_SCOPE=cross-lane
exec "$repo/Tools/NetworkBots/run-pf3-baseline.sh" "$@"
