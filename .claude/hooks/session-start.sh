#!/bin/bash
set -euo pipefail

if [ "${CLAUDE_CODE_REMOTE:-}" != "true" ]; then
  exit 0
fi

# Install .NET 10 SDK if not present
if [ ! -f "/tmp/dotnet/dotnet" ]; then
  echo "Installing .NET 10 SDK..."
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  chmod +x /tmp/dotnet-install.sh
  /tmp/dotnet-install.sh --channel 10.0 --install-dir /tmp/dotnet --no-path
fi

# Export PATH and DOTNET_ROOT for the session
echo 'export DOTNET_ROOT=/tmp/dotnet' >> "$CLAUDE_ENV_FILE"
echo 'export PATH=/tmp/dotnet:$PATH' >> "$CLAUDE_ENV_FILE"

echo ".NET version: $(/tmp/dotnet/dotnet --version)"

# Restore NuGet packages
echo "Restoring NuGet packages..."
/tmp/dotnet/dotnet restore "$CLAUDE_PROJECT_DIR/Aluki.Runtime.slnx" --verbosity quiet
