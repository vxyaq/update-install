#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
publish_dir="$project_root/installer/publish"
output_dir="$project_root/installer/output"
wix="$project_root/.codex-tools/wix/wix"

rm -rf "$publish_dir" "$output_dir"
mkdir -p "$publish_dir" "$output_dir"

dotnet publish "$project_root/AestheticOptimizer/AxeOptimizer.csproj" \
  --configuration Release \
  --runtime win-x64 \
  --self-contained true \
  --output "$publish_dir" \
  -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true \
  -p:PublishReadyToRun=false \
  -p:SkipKsyxisSecurity=true \
  -p:EnableWindowsTargeting=true

"$wix" --acceptEula wix7 build "$project_root/installer/KsyxisTweaks.wxs" \
  -d PublishDir="$publish_dir" \
  -o "$output_dir/KsyxisTweaks-1.0.0-x64.msi"

echo "Utworzono: $output_dir/KsyxisTweaks-1.0.0-x64.msi"
