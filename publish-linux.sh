#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/GraveOps.Desktop.Linux/GraveOps.Desktop.Linux.csproj"
OUTPUT="$ROOT/publish/linux-x64"

rm -rf "$OUTPUT"
mkdir -p "$OUTPUT"

dotnet publish "$PROJECT" \
  -c Release \
  -r linux-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -p:DebugType=None \
  -p:DebugSymbols=false \
  -o "$OUTPUT"

echo "Published GraveOps Linux to: $OUTPUT"