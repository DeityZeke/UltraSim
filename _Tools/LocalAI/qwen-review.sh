#!/usr/bin/env bash
set -euo pipefail

script_dir=$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)
model=${ULTRASIM_QWEN_MODEL:-ultrasim-qwen}
host=${OLLAMA_HOST:-http://127.0.0.1:11434}
context=${ULTRASIM_QWEN_CONTEXT:-16384}
max_bytes=${ULTRASIM_QWEN_MAX_INPUT_BYTES:-60000}
think=${ULTRASIM_QWEN_THINK:-false}
review_kind=${1:-ecs}

if [[ "$think" != "true" && "$think" != "false" ]]; then
    printf 'ULTRASIM_QWEN_THINK must be true or false (received: %s)\n' "$think" >&2
    exit 2
fi

if (($# > 0)); then
    shift
fi

case "$review_kind" in
    ecs) prompt_file="$script_dir/prompts/ecs-reviewer.md" ;;
    benchmark) prompt_file="$script_dir/prompts/benchmark-critic.md" ;;
    patch) prompt_file="$script_dir/prompts/patch-reviewer.md" ;;
    *)
        printf 'Unknown review kind: %s (expected ecs, benchmark, or patch)\n' "$review_kind" >&2
        exit 2
        ;;
esac

for command_name in curl jq; do
    if ! command -v "$command_name" >/dev/null 2>&1; then
        printf 'Required command is unavailable: %s\n' "$command_name" >&2
        exit 2
    fi
done

request_file=$(mktemp "${TMPDIR:-/tmp}/ultrasim-qwen-review.XXXXXX")
trap 'rm -f -- "$request_file"' EXIT

{
    printf '# Review instructions\n\n'
    sed -n '1,$p' "$prompt_file"
    printf '\n# Supplied material\n'

    if (($# == 0)); then
        printf '\n## Standard input\n\n'
        sed -n '1,$p'
    else
        for source_path in "$@"; do
            if [[ ! -f "$source_path" ]]; then
                printf 'Input file does not exist: %s\n' "$source_path" >&2
                exit 2
            fi

            printf '\n## File: %s\n\n```\n' "$source_path"
            sed -n '1,$p' "$source_path"
            printf '\n```\n'
        done
    fi
} >"$request_file"

request_bytes=$(wc -c <"$request_file")
if ((request_bytes > max_bytes)); then
    printf 'Review input is %d bytes; limit is %d. Supply a smaller, focused slice or raise ULTRASIM_QWEN_MAX_INPUT_BYTES deliberately.\n' \
        "$request_bytes" "$max_bytes" >&2
    exit 2
fi

jq -Rs \
    --arg model "$model" \
    --argjson context "$context" \
    --argjson think "$think" \
    '{
        model: $model,
        prompt: .,
        stream: false,
        think: $think,
        keep_alive: "30m",
        options: {
            num_ctx: $context,
            temperature: 0.2,
            top_p: 0.9,
            top_k: 20,
            repeat_penalty: 1.05,
            num_predict: 4096
        }
    }' <"$request_file" |
    curl --fail --silent --show-error \
        --header 'Content-Type: application/json' \
        --data-binary @- \
        "$host/api/generate" |
    jq -er '.response'
