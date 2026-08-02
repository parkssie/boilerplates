#!/usr/bin/env bash

set -euo pipefail

PROJECT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
PROJECT_FILE="$PROJECT_DIR/DotnetRestApiInfWorker.csproj"
PUBLISH_DIR="$PROJECT_DIR/_publish/windows"

if [[ ! -f "$PROJECT_FILE" ]]; then
    echo "[ERROR] Project file not found: \"$PROJECT_FILE\""
    exit 1
fi

if [[ -d "$PUBLISH_DIR" ]]; then
    echo "[INFO] Removing previous publish output..."
    rm -rf -- "$PUBLISH_DIR"
    if [[ -e "$PUBLISH_DIR" ]]; then
        echo "[ERROR] Failed to remove: \"$PUBLISH_DIR\""
        exit 1
    fi
fi

echo "[INFO] Publishing win-x64 Release build..."
if ! dotnet publish "$PROJECT_FILE" --configuration Release --output "$PUBLISH_DIR"; then
    echo "[ERROR] Publish failed."
    exit 1
fi

echo "[OK] Publish completed: \"$PUBLISH_DIR\""

