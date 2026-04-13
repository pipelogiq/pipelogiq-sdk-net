#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"
PUSH_PACKAGES="${PUSH_PACKAGES:-true}"
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT_DIR/src/Pipelogiq.Sdk/PipelogiqSDK.csproj" | head -n 1)"
OUTPUT_DIR="${OUTPUT_DIR:-$ROOT_DIR/artifacts/packages/$VERSION}"

if [[ -z "$VERSION" ]]; then
  echo "Unable to determine package version from PipelogiqSDK.csproj." >&2
  exit 1
fi

if [[ "$PUSH_PACKAGES" == "true" && -z "${NUGET_API_KEY:-}" ]]; then
  echo "NUGET_API_KEY is required." >&2
  exit 1
fi

mkdir -p "$OUTPUT_DIR"

projects=(
  "$ROOT_DIR/src/Pipelogiq.Sdk/PipelogiqSDK.csproj"
  "$ROOT_DIR/src/Pipelogiq.Sdk.Redis/Pipelogiq.Sdk.Redis.csproj"
  "$ROOT_DIR/src/Pipelogiq.Sdk.Postgres/Pipelogiq.Sdk.Postgres.csproj"
  "$ROOT_DIR/src/Pipelogiq.Sdk.Testing/Pipelogiq.Sdk.Testing.csproj"
)

dotnet restore "$ROOT_DIR/PipelogiqSdk.sln" --disable-build-servers

for project in "${projects[@]}"; do
  dotnet pack "$project" -c Release -o "$OUTPUT_DIR" --no-restore --disable-build-servers
done

if [[ "$PUSH_PACKAGES" != "true" ]]; then
  echo "Packages built in $OUTPUT_DIR"
  exit 0
fi

shopt -s nullglob

for package in "$OUTPUT_DIR"/*.nupkg; do
  dotnet nuget push "$package" \
    --source "$NUGET_SOURCE" \
    --api-key "$NUGET_API_KEY" \
    --skip-duplicate
done

for symbols in "$OUTPUT_DIR"/*.snupkg; do
  dotnet nuget push "$symbols" \
    --source "$NUGET_SOURCE" \
    --api-key "$NUGET_API_KEY" \
    --skip-duplicate
done
