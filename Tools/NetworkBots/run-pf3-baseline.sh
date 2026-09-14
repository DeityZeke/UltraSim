#!/usr/bin/env bash
set -euo pipefail

if [[ $# -lt 3 || $# -gt 17 ]]; then
    echo "usage: $0 <scenario> <duration> <output-dir> [bots=50] [seed=300301] [warmup=60s] [cache-dir] [authority-hz=60] [maximum-input-hz=60] [recommended-input-hz=30] [sample-hz=120] [submit-hz=recommended] [input-mode=compliant] [network-service-delay-ms=1] [input-burst=10] [input-burst-window-ms=100] [clusters=1]" >&2
    exit 2
fi

scenario=$1
duration=$2
output_dir=$(realpath -m "$3")
bots=${4:-50}
seed=${5:-300301}
warmup=${6:-60s}
cache_dir=$(realpath -m "${7:-/tmp/ultrasim-pf3-shared-cache}")
authority_hz=${8:-60}
maximum_input_hz=${9:-60}
recommended_input_hz=${10:-30}
sample_hz=${11:-120}
submit_hz=${12:-$recommended_input_hz}
input_mode=${13:-compliant}
network_service_delay_ms=${14:-1}
input_burst=${15:-10}
input_burst_window_ms=${16:-100}
clusters=${17:-1}
checkpoint=${ULTRASIM_BOT_CHECKPOINT:-PF3}
telemetry=${ULTRASIM_TELEMETRY:-Detailed}
accounting_test_marker_args=()
same_address_admission_burst=${ULTRASIM_SAME_ADDRESS_ADMISSION_BURST:-128}
case "$checkpoint" in
    PF3) summary_schema=5 ;;
    PF4A) summary_schema=6 ;;
    PF4B) summary_schema=7 ;;
    PF4C) summary_schema=8 ;;
    PF4D) summary_schema=9 ;;
    PF4E) summary_schema=10 ;;
    PF4F) summary_schema=11 ;;
    PF5) summary_schema=12 ;;
    *) echo "unsupported bot evidence checkpoint: $checkpoint" >&2; exit 2 ;;
esac
if [[ "$checkpoint" == "PF5" ]]; then
    accounting_test_marker_args+=(--accounting-test-marker="PF5 $scenario")
fi

case "$scenario" in
    quiet|sustained-input|moving|crowd|single-hot|multi-hot|mixed-chaos|clusters|migration-converge|migration-disperse|chat-swarm|inventory-swarm|vertical-swarm|reconnect-swarm|connection-churn) ;;
    *) echo "unsupported PF3 scenario: $scenario" >&2; exit 2 ;;
esac
if [[ -e "$output_dir" ]]; then
    echo "output directory already exists: $output_dir" >&2
    exit 2
fi
mkdir -p "$output_dir" "$cache_dir"

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

wait_measurement_complete() {
    local path=$1
    local seconds=$2
    local failure_summary=$3
    local deadline=$((SECONDS + seconds))
    while [[ ! -f "$path" ]]; do
        if [[ -f "$failure_summary" ]]; then
            echo "bot harness exited before producing $path; see $failure_summary" >&2
            return 1
        fi
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
    --maximum-participants="$bots" \
    --same-address-admission-burst="$same_address_admission_burst" \
    --authority-hz="$authority_hz" \
    --maximum-input-hz="$maximum_input_hz" \
    --recommended-input-hz="$recommended_input_hz" \
    --input-burst="$input_burst" --input-burst-window-ms="$input_burst_window_ms" \
    --network-service-delay-ms="$network_service_delay_ms" \
    --public-representation="${ULTRASIM_PUBLIC_REPRESENTATION:-message129}" \
    --public-cadence="${ULTRASIM_PUBLIC_CADENCE:-1/2/4}" \
    --public-compression="${ULTRASIM_PUBLIC_COMPRESSION:-off}" \
    --unreliable-frame-cap="${ULTRASIM_UNRELIABLE_FRAME_CAP:-2}" \
    --unreliable-aggregation-scope="${ULTRASIM_UNRELIABLE_AGGREGATION_SCOPE:-cross-lane}" \
    --telemetry="$telemetry" "${accounting_test_marker_args[@]}" \
    >"$output_dir/server.log" 2>&1 &
server_pid=$!
wait_file "$server_ready" 60

endpoint=$(jq -er '.Endpoint' "$server_ready")
fingerprint=$(jq -er '.Fingerprint' "$server_ready")
bot_arguments=(
    --endpoint="$endpoint" --fingerprint="$fingerprint"
    --bots="$bots" --seed="$seed" --duration="$duration" --warmup="$warmup"
    --scenario="$scenario" --cache="$cache_dir" --admission-stagger-ms=100
    --measurement-ready-file="$measurement_ready"
    --measurement-release-file="$measurement_release"
    --measurement-complete-file="$measurement_complete"
    --ready-file="$bot_ready" --release-file="$bot_release"
    --summary-file="$bot_summary" --telemetry="$telemetry"
    --unreliable-frame-cap="${ULTRASIM_UNRELIABLE_FRAME_CAP:-2}"
    --unreliable-aggregation-scope="${ULTRASIM_UNRELIABLE_AGGREGATION_SCOPE:-cross-lane}"
    --checkpoint="$checkpoint"
)
if [[ "$checkpoint" == "PF5" ]]; then
    bot_arguments+=(--clusters="$clusters")
fi
if [[ "$scenario" == "sustained-input" ]]; then
    bot_arguments+=(--input-sample-hz="$sample_hz" --input-submit-hz="$submit_hz" --input-mode="$input_mode")
fi

dotnet run --project Tools/NetworkBots/UltraSim.NetworkBots.csproj \
    -c Release -p:UltraSimNetworkFlavor=ClientOnly --no-build -- \
    "${bot_arguments[@]}" >"$output_dir/bot.log" 2>&1 &
bot_pid=$!

wait_file "$measurement_ready" 600
touch "$accounting_start"
wait_file "$accounting_ready" 30
touch "$measurement_release"
wait_measurement_complete "$measurement_complete" 90000 "$bot_summary"
touch "$accounting_stop"
wait_file "$accounting_summary" 60
wait_measurement_complete "$bot_ready" 60 "$bot_summary"
touch "$bot_release"
wait "$bot_pid"
bot_pid=
touch "$server_stop"
wait "$server_pid"
server_pid=

jq -e --arg checkpoint "$checkpoint" --argjson schema "$summary_schema" \
    '.passed and .cleanRetirement and .checkpoint == $checkpoint and .schemaVersion == $schema and
     (($checkpoint != "PF4A" and $checkpoint != "PF4B" and $checkpoint != "PF4C" and $checkpoint != "PF4D" and $checkpoint != "PF4E" and $checkpoint != "PF4F" and $checkpoint != "PF5") or
      ([.actionOutcomes[] | select((.conserved | not) or (.terminal | not))] | length) == 0)' \
    "$bot_summary" >/dev/null
jq -e '.PhysicalTransportReconciled and .LogicalMessageReconciled and .ParticipantsAtEnd == .ParticipantsAtStart' \
    "$accounting_summary" >/dev/null
if [[ "$checkpoint" == "PF5" ]]; then
    jq -e '
      (.TopologySamplesSaturated | not) and
      (.TopologySamples | length) >= 2 and
      ([.TopologySamples[] | select(.RemoteRecipients > 0 and .OccupiedChunks > 0 and .Memberships > 0)] | length) > 0 and
      .RemoteReplication.SourceCommitGaps == 0 and
      .HttpBody.ResponseBodyBytes == 0
    ' "$accounting_summary" >/dev/null
fi
jq -e '.CleanRetirement and .Participants == 0 and .EstablishedSessions == 0' \
    "$server_summary" >/dev/null

trap - EXIT INT TERM
echo "$checkpoint baseline complete: $output_dir"
