#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
CONFIG="${1:-Release}"

echo "========================================="
echo " Building AIOTweaks (${CONFIG})"
echo "========================================="

# Locate dotnet binary
DOTNET_BIN=""
if command -v dotnet &> /dev/null; then
    DOTNET_BIN="$(command -v dotnet)"
elif [ -f "$HOME/.dotnet/dotnet" ]; then
    DOTNET_BIN="$HOME/.dotnet/dotnet"
elif [ -f "/usr/share/dotnet/dotnet" ]; then
    DOTNET_BIN="/usr/share/dotnet/dotnet"
fi

if [ -z "$DOTNET_BIN" ]; then
    echo "Error: 'dotnet' command not found in PATH or standard install locations."
    echo "Please ensure the .NET 9.0 SDK is installed."
    exit 1
fi

echo "Using .NET CLI: $($DOTNET_BIN --version 2>/dev/null || echo "$DOTNET_BIN")"

# Candidate search directories for game assemblies
STEAM_CANDIDATE_PATHS=(
    "/mnt/data/SteamLibrary"
    "$HOME/.local/share/Steam"
    "$HOME/.steam/steam"
    "$HOME/.steam/root"
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"
)

MSBUILD_PROPS=""

# Detect sts2.dll if not explicitly provided
if [ -z "$STS2_PATH" ]; then
    for base in "${STEAM_CANDIDATE_PATHS[@]}"; do
        candidate="$base/steamapps/common/Slay the Spire 2/data_sts2_linuxbsd_x86_64/sts2.dll"
        if [ -f "$candidate" ]; then
            STS2_PATH="$candidate"
            break
        fi
    done
fi

if [ -n "$STS2_PATH" ] && [ -f "$STS2_PATH" ]; then
    echo "Found sts2.dll: $STS2_PATH"
    MSBUILD_PROPS="$MSBUILD_PROPS -p:Sts2Path=\"$STS2_PATH\""
fi

# Detect BaseLib.dll if not explicitly provided
if [ -z "$BASELIB_PATH" ]; then
    for base in "${STEAM_CANDIDATE_PATHS[@]}"; do
        candidate="$base/steamapps/workshop/content/2868840/3737335127/BaseLib/BaseLib.dll"
        if [ -f "$candidate" ]; then
            BASELIB_PATH="$candidate"
            break
        fi
    done
fi

if [ -n "$BASELIB_PATH" ] && [ -f "$BASELIB_PATH" ]; then
    echo "Found BaseLib.dll: $BASELIB_PATH"
    MSBUILD_PROPS="$MSBUILD_PROPS -p:BaseLibPath=\"$BASELIB_PATH\""
fi

# Restore and compile solution
echo "Restoring packages..."
eval "\"$DOTNET_BIN\" restore \"$SCRIPT_DIR/aiotweaks.sln\""

echo "Compiling project..."
eval "\"$DOTNET_BIN\" build \"$SCRIPT_DIR/aiotweaks.sln\" -c \"$CONFIG\" $MSBUILD_PROPS"

# Locate build output directory
OUTPUT_DIR=""
if [ -d "$SCRIPT_DIR/src/.godot/mono/temp/bin/$CONFIG" ]; then
    OUTPUT_DIR="$SCRIPT_DIR/src/.godot/mono/temp/bin/$CONFIG"
elif [ -d "$SCRIPT_DIR/src/bin/$CONFIG/net9.0" ]; then
    OUTPUT_DIR="$SCRIPT_DIR/src/bin/$CONFIG/net9.0"
elif [ -d "$SCRIPT_DIR/bin/$CONFIG/net9.0" ]; then
    OUTPUT_DIR="$SCRIPT_DIR/bin/$CONFIG/net9.0"
fi

echo "========================================="
echo " Build Completed Successfully!"
echo " Output Location: ${OUTPUT_DIR:-$SCRIPT_DIR/src/.godot/mono/temp/bin/$CONFIG}"
if [ -n "$OUTPUT_DIR" ] && [ -d "$OUTPUT_DIR" ]; then
    echo " Built Files:"
    ls -lh "$OUTPUT_DIR"/AIOTweaks.* 2>/dev/null || ls -la "$OUTPUT_DIR"
fi
echo "========================================="
