#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 3 || $# -gt 7 ]]; then
    echo "usage: $0 <quiet|moving|crowd|mixed-chaos> <duration> <output-dir> [bots=50] [seed=130363] [warmup=60s] [cache-dir]" >&2
    exit 2
fi

scenario=$1
duration=$2
output_dir=$(realpath -m "$3")
bots=${4:-50}
seed=${5:-130363}
warmup=${6:-60s}
cache_dir=$(realpath -m "${7:-/tmp/ultrasim-pf25-shared-cache}")

case "$scenario" in quiet|moving|crowd|mixed-chaos) ;; *) echo "unsupported scenario: $scenario" >&2; exit 2;; esac
if [[ -e "$output_dir" ]]; then
    echo "output directory already exists: $output_dir" >&2
    exit 2
fi
mkdir -p "$output_dir" "$cache_dir"

# Direct Connect permits an ephemeral UDP listener, while the HTTP content host requires
# a concrete nonzero port. Ask the kernel for a free loopback TCP port for this isolated run.
content_port=$(python3 -c 'import socket; s=socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')

repo=$(git rev-parse --show-toplevel)
server_ready="$output_dir/server-ready.json"
server_stop="$output_dir/server-stop"
server_summary="$output_dir/server-summary.json"
accounting_start="$output_dir/accounting-start"
accounting_ready="$output_dir/accounting-ready.json"
accounting_stop="$output_dir/accounting-stop"
accounting_summary="$output_dir/accounting-summary.json"
measurement_ready="$output_dir/measurement-ready"
measurement_release="$output_dir/measurement-release"
measurement_complete="$output_dir/measurement-complete"
bot_ready="$output_dir/bot-ready"
bot_release="$output_dir/bot-release"
bot_summary="$output_dir/bot-summary.json"
server_pid=
bot_pid=

cleanup() {
    touch "$measurement_release" "$bot_release" "$accounting_stop" "$server_stop" 2>/dev/null || true
    if [[ -n "$bot_pid" ]]; then kill "$bot_pid" 2>/dev/null || true; wait "$bot_pid" 2>/dev/null || true; fi
    if [[ -n "$server_pid" ]]; then kill "$server_pid" 2>/dev/null || true; wait "$server_pid" 2>/dev/null || true; fi
}
trap cleanup EXIT INT TERM

wait_file() {
    local path=$1
    local seconds=$2
    local deadline=$((SECONDS + seconds))
    while [[ ! -f "$path" ]]; do
        if (( SECONDS >= deadline )); then
            echo "timed out waiting for $path" >&2
            return 1
        fi
        sleep 0.2
    done
}

cd "$repo"
dotnet run --project Server/DirectConnect/UltraSim.Server.DirectConnect.csproj \
    -c Release -p:UltraSimNetworkFlavor=ServerOnly --no-build -- \
    --bind=127.0.0.1:0 --content-bind="127.0.0.1:$content_port" \
    --identity-dir="$output_dir/deployment" \
    --ready-file="$server_ready" --stop-file="$server_stop" \
    --summary-file="$server_summary" \
    --accounting-start-file="$accounting_start" \
    --accounting-ready-file="$accounting_ready" \
    --accounting-stop-file="$accounting_stop" \
    --accounting-summary-file="$accounting_summary" \
    --maximum-participants="$bots" --same-address-admission-burst=128 \
    --telemetry=Detailed >"$output_dir/server.log" 2>&1 &
server_pid=$!
wait_file "$server_ready" 60

endpoint=$(jq -er '.Endpoint' "$server_ready")
fingerprint=$(jq -er '.Fingerprint' "$server_ready")

dotnet run --project Tools/NetworkBots/UltraSim.NetworkBots.csproj \
    -c Release -p:UltraSimNetworkFlavor=ClientOnly --no-build -- \
    --endpoint="$endpoint" --fingerprint="$fingerprint" \
    --bots="$bots" --seed="$seed" --duration="$duration" --warmup="$warmup" \
    --scenario="$scenario" --cache="$cache_dir" --admission-stagger-ms=100 \
    --measurement-ready-file="$measurement_ready" \
    --measurement-release-file="$measurement_release" \
    --measurement-complete-file="$measurement_complete" \
    --ready-file="$bot_ready" --release-file="$bot_release" \
    --summary-file="$bot_summary" --telemetry=Detailed \
    >"$output_dir/bot.log" 2>&1 &
bot_pid=$!

wait_file "$measurement_ready" 240
touch "$accounting_start"
wait_file "$accounting_ready" 30
touch "$measurement_release"

# Duration may be as large as 24 hours. The bot process itself owns the exact boundary;
# this outer guard merely leaves ample time for that configured window plus quiescence.
wait_file "$measurement_complete" 90000
touch "$accounting_stop"
wait_file "$accounting_summary" 60
wait_file "$bot_ready" 60
touch "$bot_release"
wait "$bot_pid"
bot_pid=
touch "$server_stop"
wait "$server_pid"
server_pid=

jq -e '.passed and .cleanRetirement and .checkpoint == "PF2.5" and .schemaVersion == 4' \
    "$bot_summary" >/dev/null
jq -e '.PhysicalTransportReconciled and .LogicalMessageReconciled and .ParticipantsAtEnd == .ParticipantsAtStart' \
    "$accounting_summary" >/dev/null
jq -e '.CleanRetirement and .Participants == 0 and .EstablishedSessions == 0' \
    "$server_summary" >/dev/null

trap - EXIT INT TERM
echo "PF2.5 baseline complete: $output_dir"
