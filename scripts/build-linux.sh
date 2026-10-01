#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
project="$repo_root/Linux/EzPocket.Linux.csproj"

dotnet restore "$project"
dotnet build "$project" --no-restore "$@"
