#!/usr/bin/env bash
set -e

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# Parse build arguments
CONFIG="Release"
DEPLOY=false
NUKE_LOGS=false
EXPLICIT_BUILD_ACTION=false

# Candidate search directories for game assemblies and log cleanup
STEAM_CANDIDATE_PATHS=(
    "/mnt/data/SteamLibrary"
    "$HOME/.local/share/Steam"
    "$HOME/.steam/steam"
    "$HOME/.steam/root"
    "$HOME/.var/app/com.valvesoftware.Steam/.local/share/Steam"
)

nuke_debug_logs() {
    echo "========================================="
    echo " Nuking debug log files and crash dumps..."
    echo "========================================="
    local count=0

    local SEARCH_DIRS=(
        "$SCRIPT_DIR"
        "$SCRIPT_DIR/src"
        "$SCRIPT_DIR/src/.godot/mono/temp/bin/Debug"
        "$SCRIPT_DIR/src/.godot/mono/temp/bin/Release"
        "$HOME/.local/share/godot/app_userdata/Slay the Spire 2"
        "$HOME/.local/share/godot/app_userdata/sts2"
    )

    if [ -n "$STS2_MOD_DIR" ] && [ -d "$STS2_MOD_DIR" ]; then
        SEARCH_DIRS+=("$STS2_MOD_DIR")
    fi

    for base in "${STEAM_CANDIDATE_PATHS[@]}"; do
        SEARCH_DIRS+=(
            "$base/steamapps/common/Slay the Spire 2"
            "$base/steamapps/common/Slay the Spire 2/mods/AIOTweaks"
            "$base/steamapps/workshop/content/2868840/3795280483"
        )
    done

    for dir in "${SEARCH_DIRS[@]}"; do
        if [ -d "$dir" ]; then
            while IFS= read -r -d '' file; do
                echo " [Nuke] Removing: $file"
                rm -f "$file"
                count=$((count + 1))
            done < <(find "$dir" -maxdepth 4 \( -name "aiotweaks_debug.log*" -o -name "aiotweaks_crash_*.txt" -o -name "aiotweaks_crash*.log" -o -name "aiotweaks_run_*.log" \) -print0 2>/dev/null)

            # Clean up empty logs/runs directory if left over
            if [ -d "$dir/logs/runs" ]; then
                rmdir "$dir/logs/runs" 2>/dev/null || true
            fi
            if [ -d "$dir/logs" ]; then
                rmdir "$dir/logs" 2>/dev/null || true
            fi
        fi
    done

    if [ "$count" -eq 0 ]; then
        echo " [Nuke] No stale log or crash dump files found."
    else
        echo " [Nuke] Successfully nuked $count log/crash dump file(s)!"
    fi
    echo "========================================="
}

for arg in "$@"; do
    case "${arg,,}" in
        debug|-d|--debug)
            CONFIG="Debug"
            EXPLICIT_BUILD_ACTION=true
            ;;
        release|-r|--release)
            CONFIG="Release"
            EXPLICIT_BUILD_ACTION=true
            ;;
        deploy|-dp|--deploy)
            DEPLOY=true
            EXPLICIT_BUILD_ACTION=true
            ;;
        nuke-logs|--nuke-logs|-nl)
            NUKE_LOGS=true
            ;;
        -h|--help|help)
            echo "========================================="
            echo " AIOTweaks Build Script"
            echo "========================================="
            echo "Usage: ./build.sh [Configuration] [--deploy] [--nuke-logs]"
            echo ""
            echo "Options:"
            echo "  Release, -r,  --release      (Default) Build optimized Release binary."
            echo "  Debug,   -d,  --debug        Build Debug binary (forcefully enables verbose"
            echo "                               logging and saves logs to aiotweaks_debug.log"
            echo "                               in the mod's root folder)."
            echo "  Deploy,  -dp, --deploy       Deploy built DLLs to the game mod folder if found."
            echo "  Nuke,    -nl, --nuke-logs    Nuke/delete all aiotweaks_debug.log and"
            echo "                               crash dump text files from build and game folders."
            echo "  -h,           --help         Display this help message."
            echo ""
            echo "Environment Variables (Optional):"
            echo "  STS2_PATH      Explicit path to sts2.dll"
            echo "  BASELIB_PATH   Explicit path to BaseLib.dll"
            echo "  STS2_MOD_DIR   Explicit path to target AIOTweaks mod directory"
            echo "========================================="
            exit 0
            ;;
        *)
            # Allow positional configuration if it doesn't start with -
            if [[ "$arg" != -* ]]; then
                CONFIG="$arg"
                EXPLICIT_BUILD_ACTION=true
            fi
            ;;
    esac
done

if [ "$NUKE_LOGS" = true ]; then
    nuke_debug_logs
    # If the user only called nuke-logs without requesting a build or deploy, exit cleanly
    if [ "$EXPLICIT_BUILD_ACTION" = false ] && [ "$#" -eq 1 ]; then
        exit 0
    fi
fi

echo "========================================="
if [ "$CONFIG" = "Debug" ]; then
    echo " Building AIOTweaks (${CONFIG}) [Debug Mode: Force Verbose + File Logging]"
else
    echo " Building AIOTweaks (${CONFIG})"
fi
if [ "$DEPLOY" = true ]; then
    echo " Deployment: Enabled (--deploy)"
else
    echo " Deployment: Disabled (pass --deploy or -dp to copy DLLs to game folder)"
fi
if [ "$NUKE_LOGS" = true ]; then
    echo " Log Cleanup: Executed (--nuke-logs)"
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

    # Deploy to game mods directory if requested
    if [ "$DEPLOY" = true ]; then
        TARGET_MOD_DIR=""
        if [ -n "$STS2_MOD_DIR" ] && [ -d "$STS2_MOD_DIR" ]; then
            TARGET_MOD_DIR="$STS2_MOD_DIR"
        else
            for base in "${STEAM_CANDIDATE_PATHS[@]}"; do
                candidate_mod_dir="$base/steamapps/common/Slay the Spire 2/mods/AIOTweaks"
                if [ -d "$candidate_mod_dir" ]; then
                    TARGET_MOD_DIR="$candidate_mod_dir"
                    break
                fi
            done
        fi

        DEPLOYED_COUNT=0
        if [ -n "$TARGET_MOD_DIR" ] && [ -d "$TARGET_MOD_DIR" ]; then
            echo " [Deploy] Target mod folder verified: $TARGET_MOD_DIR"
            echo " [Deploy] Deploying built DLLs..."
            cp -vf "$OUTPUT_DIR"/AIOTweaks.* "$TARGET_MOD_DIR/"
            DEPLOYED_COUNT=$((DEPLOYED_COUNT + 1))
        fi

        if [ "$DEPLOYED_COUNT" -gt 0 ]; then
            echo " [Deploy] Deployment completed successfully to game mods folder!"
        else
            echo " [Deploy] Warning: Target game mod folder not found."
            echo "          Expected: .../Slay the Spire 2/mods/AIOTweaks"
            echo "          Please ensure the game and mods/AIOTweaks folder exist, or set STS2_MOD_DIR."
        fi
    fi
fi
echo "========================================="
