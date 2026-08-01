#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PROJECT="$ROOT/src/GraveOps.Desktop.Linux/GraveOps.Desktop.Linux.csproj"

dotnet run --project "$PROJECT"