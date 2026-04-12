#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUTPUT_DIR="${OUTPUT_DIR:-$ROOT_DIR/artifacts/packages}"
NUGET_SOURCE="${NUGET_SOURCE:-https://api.nuget.org/v3/index.json}"

if [[ -z "${NUGET_API_KEY:-}" ]]; then
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

dotnet restore "$ROOT_DIR/PipelogiqSdk.sln"

for project in "${projects[@]}"; do
  dotnet pack "$project" -c Release -o "$OUTPUT_DIR" --no-restore
done

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
