#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
edition="${1:-}"

case "$edition" in
  SinglePlayer)
    preset="UltraSim Single Player Linux"
    default_output="$script_dir/.output/clients/single-player/UltraSimSinglePlayer.x86_64"
    ;;
  Online)
    preset="UltraSim Online Linux"
    default_output="$script_dir/.output/clients/online/UltraSimOnline.x86_64"
    ;;
  Complete)
    preset="UltraSim Complete Linux"
    default_output="$script_dir/.output/clients/complete/UltraSimComplete.x86_64"
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
mkdir -p -- "$(dirname -- "$output")"

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
"$godot" --headless --path "$script_dir" --export-release "$preset" "$output"
