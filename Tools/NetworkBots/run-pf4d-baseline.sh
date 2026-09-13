#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
ULTRASIM_BOT_CHECKPOINT=PF4D exec "$script_dir/run-pf3-baseline.sh" "$@"
