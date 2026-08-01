#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/GraveOps.Desktop.Linux/GraveOps.Desktop.Linux.csproj"

echo "=== GraveOps Linux build ==="
dotnet restore "$PROJECT"
dotnet build "$PROJECT" -c Release --no-restore
echo "Build completed."