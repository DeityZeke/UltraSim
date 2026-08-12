#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
launch_dir="$PWD"
project="$script_dir/UltraSim.ECS.ExtremeSphere.Godot.csproj"
godot_bin="${GODOT_BIN:-/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64}"
headless=()

if [[ "${1:-}" == "--godot-headless" ]]; then
    headless=(--headless)
    shift
fi

benchmark_args=()
for argument in "$@"; do
    if [[ "$argument" == --output=* ]]; then
        output_path="${argument#--output=}"
        if [[ "$output_path" != /* ]]; then
            argument="--output=$launch_dir/$output_path"
        fi
    fi
    benchmark_args+=("$argument")
done

if [[ ! -x "$godot_bin" ]]; then
    echo "Godot executable not found or not executable: $godot_bin" >&2
    exit 2
fi

dotnet build "$project" -c Debug --nologo -v:minimal
dotnet build "$project" -c Release --nologo -v:minimal

debug_dir="$script_dir/.godot/mono/temp/bin/Debug"
release_dir="$script_dir/.godot/mono/temp/bin/Release"
for assembly in \
    UltraSim.ECS.ExtremeSphere.Godot.dll \
    UltraSim.ECS.ExtremeSphere.Core.dll \
    UltraSim.ECS.dll
do
    cp "$release_dir/$assembly" "$debug_dir/$assembly"
done

# This is an editor-hosted Godot run using Release-managed assemblies. A true
# Release export requires matching 4.7.1.stable.mono export templates.
exec "$godot_bin" "${headless[@]}" --path "$script_dir" -- "${benchmark_args[@]}"
