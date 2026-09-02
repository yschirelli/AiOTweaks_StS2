#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Parse build arguments
CONFIG="Release"
case "${1,,}" in
    debug|-d|--debug)
        CONFIG="Debug"
        ;;
    release|-r|--release|"")
        CONFIG="Release"
        ;;
    -h|--help|help)
        echo "========================================="
        echo " AIOTweaks Build Script"
        echo "========================================="
        echo "Usage: ./build.sh [Configuration]"
        echo ""
        echo "Options:"
        echo "  Release, -r, --release   (Default) Build optimized Release binary."
        echo "  Debug,   -d, --debug     Build Debug binary (forcefully enables verbose"
        echo "                           logging and saves logs to aiotweaks_debug.log"
        echo "                           in the mod's root folder)."
        echo "  -h,      --help          Display this help message."
        echo ""
        echo "Environment Variables (Optional):"
        echo "  STS2_PATH      Explicit path to sts2.dll"
        echo "  BASELIB_PATH   Explicit path to BaseLib.dll"
        echo "========================================="
        exit 0
        ;;
    *)
        CONFIG="$1"
        ;;
esac

echo "========================================="
if [ "$CONFIG" = "Debug" ]; then
    echo " Building AIOTweaks (${CONFIG}) [Debug Mode: Force Verbose + File Logging]"
else
    echo " Building AIOTweaks (${CONFIG})"
fi
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

# Restore if needed and compile solution
if [ ! -f "$SCRIPT_DIR/obj/project.assets.json" ] && [ ! -f "$SCRIPT_DIR/src/.godot/mono/temp/obj/project.assets.json" ]; then
    echo "Restoring packages..."
    "$DOTNET_BIN" restore "$SCRIPT_DIR/aiotweaks.sln"
fi

echo "Compiling project..."
eval "\"$DOTNET_BIN\" build \"$SCRIPT_DIR/aiotweaks.sln\" -c \"$CONFIG\" $MSBUILD_PROPS --no-restore"

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
