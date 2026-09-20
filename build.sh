#! /usr/bin/env nix-shell
#! nix-shell -i bash -p dotnet-sdk_8 python3
set -euo pipefail
cd "$(dirname "$0")"
: "${MELONLOADER_DIR:?Set MELONLOADER_DIR to the profile MelonLoader directory}"
dotnet build CasinoLedger.csproj --configuration Release "-p:MelonLoaderDir=$MELONLOADER_DIR"
python3 package.py --prepare
