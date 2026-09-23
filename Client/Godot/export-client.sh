#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
edition="${1:-}"

case "$edition" in
  SinglePlayer)
    preset="UltraSim Single Player Linux"
    default_output="$script_dir/.output/clients/single-player/UltraSimSinglePlayer.x86_64"
    network_flavor="Full"
    ;;
  Online)
    preset="UltraSim Online Linux"
    default_output="$script_dir/.output/clients/online/UltraSimOnline.x86_64"
    network_flavor="ClientOnly"
    ;;
  Complete)
    preset="UltraSim Complete Linux"
    default_output="$script_dir/.output/clients/complete/UltraSimComplete.x86_64"
    network_flavor="Full"
    ;;
  *)
    echo "usage: $0 SinglePlayer|Online|Complete [output-path]" >&2
    exit 2
    ;;
esac

godot="${ULTRASIM_GODOT:-/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64}"
if [[ ! -x "$godot" ]]; then
  echo "Godot executable not found; set ULTRASIM_GODOT: $godot" >&2
  exit 2
fi

output="${2:-$default_output}"
output_dir="$(dirname -- "$output")"
managed_output="$output_dir/data_UltraSim.Client.Application.Godot_linuxbsd_x86_64"
dependency_manifest="$managed_output/UltraSim.Client.Application.Godot.deps.json"
mkdir -p -- "$output_dir"

export_release_bin="$script_dir/.godot/mono/temp/bin/ExportRelease"
export_release_obj="$script_dir/.godot/mono/temp/obj/ExportRelease"

# Edition changes alter the root project graph. Remove only its generated ExportRelease state so a
# previous wider product cannot leave stale managed files for the next export. The exported product
# owns copied outputs, so clear the transient RID-specific script assembly afterward as well; Godot's
# editor scanner must never discover it beside the normal development assembly.
cleanup_export_state() {
  rm -rf -- "$export_release_bin" "$export_release_obj"
}
cleanup_export_state
trap cleanup_export_state EXIT

export UltraSimEdition="$edition"
export UltraSimNetworkFlavor="$network_flavor"

restore_export_graph() {
  # Godot publishes with a runtime identifier and may otherwise reuse the root project's
  # project.assets.json from the previously exported edition. Because edition selection changes
  # the root project graph, force an exact restore before its no-restore publish step.
  dotnet restore "$script_dir/UltraSim.Client.Application.Godot.csproj" \
    -r linux-x64 \
    --force \
    -p:UltraSimEdition="$edition" \
    -p:UltraSimNetworkFlavor="$network_flavor"
}

run_export() {
  "$godot" --headless --path "$script_dir" --export-release "$preset" "$output"
}

restore_export_graph
run_export

# Godot 4.7 can report a successful export process after its internal managed publish has failed,
# leaving only the native shell/PCK. Detect that exact partial state, retry once from bounded
# generated state, and keep a repeated failure visible rather than publishing an incomplete client.
if [[ ! -f "$output" || ! -f "$dependency_manifest" ]]; then
  echo "Managed export was incomplete; retrying once from clean generated state." >&2
  cleanup_export_state
  rm -f -- "$output"
  rm -rf -- "$managed_output"
  restore_export_graph
  run_export
fi
if [[ ! -f "$output" || ! -f "$dependency_manifest" ]]; then
  echo "Managed export is incomplete after retry: $output" >&2
  exit 1
fi

official_root="$output_dir/Content/Official"
if [[ "$edition" == "Online" ]]; then
  rm -f -- "$official_root/ultrasim-official-actors-v1.uspkg"
else
  dotnet run \
    --project "$script_dir/../../_Tools/OfficialContentBuilder/UltraSim.OfficialContentBuilder.csproj" \
    -c Release -- "$official_root" "$script_dir/../PresentationAssets"
fi
