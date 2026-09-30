#!/usr/bin/env bash
set -euo pipefail

script_dir="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
repository_root="$(cd -- "$script_dir/../.." && pwd)"
output_root="${1:-$repository_root/.output/servers/gui}"
godot_binary="${ULTRASIM_GODOT:-godot}"

mkdir -p "$output_root/headless"
dotnet publish "$repository_root/Server/DirectConnect/UltraSim.Server.DirectConnect.csproj" \
    -c Release -p:UltraSimNetworkFlavor=ServerOnly --output "$output_root/headless"
mkdir -p "$output_root/headless/Content/Official"
cp "$repository_root/Client/OfficialContent/ultrasim-official-actors-v1.uspkg" \
    "$output_root/headless/Content/Official/ultrasim-official-actors-v1.uspkg"
chmod +x "$output_root/headless/run-managed.sh"
"$godot_binary" --headless --path "$script_dir" --editor --quit
dotnet build "$script_dir/UltraSim.Server.Operator.Godot.csproj" -c ExportRelease
"$godot_binary" --headless --path "$script_dir" --export-release \
    "UltraSim Server GUI Linux" "$output_root/UltraSimServerGui.x86_64"

printf 'UltraSim server GUI product: %s\n' "$output_root/UltraSimServerGui.x86_64"
