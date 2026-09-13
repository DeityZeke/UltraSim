#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
export ULTRASIM_BOT_CHECKPOINT=PF4E
export ULTRASIM_TELEMETRY=${ULTRASIM_TELEMETRY:-Detailed}
export ULTRASIM_PUBLIC_REPRESENTATION=message129
export ULTRASIM_PUBLIC_CADENCE=1/2/4
export ULTRASIM_UNRELIABLE_FRAME_CAP=2
export ULTRASIM_UNRELIABLE_AGGREGATION_SCOPE=cross-lane
exec "$script_dir/run-pf3-baseline.sh" "$@"
