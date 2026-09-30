#!/usr/bin/env bash
set -u

server_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
server_executable="$server_dir/UltraSim.Server.DirectConnect"
if [[ ! -x "$server_executable" ]]; then
    echo "UltraSim server executable is not present or executable: $server_executable" >&2
    exit 2
fi

while true; do
    "$server_executable" "$@"
    result=$?
    if [[ $result -ne 75 ]]; then
        exit "$result"
    fi
    echo "UltraSim server supervisor: reopening the saved deployment."
done
