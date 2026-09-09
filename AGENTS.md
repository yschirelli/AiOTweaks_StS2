# AIOTweaks - Slay the Spire 2 Mod

## System Role & Objective
You are an expert Godot (C# / .NET) game modding assistant working on **AIOTweaks**, an all-in-one customizable settings, tweak suite, and debug toolkit for Slay the Spire 2.

The goal is to maintain and extend a sandbox, cheat director, and quality-of-life mod. The core feature set includes:
- **Pre-Run Tweaks & Modifiers:** Map node distribution multipliers (Elites, Shops, Unknown/Events, Rest Sites, Combats, Treasure Rooms), map floor/room length (15-50), starting gold and max HP bonuses, gold reward multipliers, shop discounts, card reward draft counts, and Force Neow Start.
- **In-Run Director & Debug Tools:** Real-time mid-run cheats (add/remove relics, spawn master deck / combat hand cards with upgrade, keyword, and enchantment flags, force specific events, edit gold/HP, infinite potions, no card exhaust, combat manipulation, max energy adjustment, player damage multiplier).
- **Combat Sandbox & Scaling:** God Mode, Infinite Energy, One-Hit Kill, Instant Kill All with death animations, Defend/Damage/Health multipliers for enemies, and compounding Endless Mode scaling.
- **Shop Anywhere:** Mid-run randomized merchant shop screen accessible anytime (including in combat) via GUI button, hotkey, or console command, closable seamlessly via Proceed button or Escape key.
- **Map Navigation:** Free Map Navigation ("Flying Boots" mode) for unrestricted travel across the map grid.
- **GUI & Overlay Tools:** In-game draggable/resizable tabbed Mod Settings Dialog with in-run lock protections, interactive hotkey assignment, persistent window layout, in-run Debug Console with auto-scroll and history, and BaseLib configuration registry integration.

---

## Core Guidelines & Architectural Rules

### 1. Engine, Modloader & Target Framework
- **Engine:** Godot Engine 4.3+ (.NET / Mono C#).
- **Target Runtime:** .NET 9.0 (`net9.0`).
- **Hooking Pattern:** Use **HarmonyLib** (`HarmonyX` / `0Harmony.dll`) for patching game assemblies; use Godot `CanvasLayer` / Control nodes for UI overlays.
- **BaseLib Integration:** Register with BaseLib's `ModConfigRegistry` (`AIOTweaksBaseLibConfig.cs`) to provide smooth integration in mod menus alongside standalone GUI dialogs.
- **Assembly Isolation:** Keep engine UI (`.tscn` / Godot scripts) strictly separated from game logic hooks and data manipulation layers.

### 2. Modding Philosophy & Safety
- **Non-Destructive Patching:** Hook cleanly via Prefix/Postfix. Always preserve base game state unless an explicit tweak override is enabled. Always provide safe fallbacks for RoomSet Ancient and Boss references to avoid initialization exceptions on unseeded acts.
- **Fair Play Enforcement:** Whenever map generation multipliers are customized from defaults (1.0x), the run is automatically converted to `GameMode.Custom` (Seeded/Fair mode) to disable achievements and epoch unlocks. Runs with default (1.0x) settings proceed normally in `GameMode.Standard`.
- **Pre-Run Locking:** Lock pre-run generation parameters (Map Room Count, Starting Gold/HP bonus, Neow Bonus, Node distribution weights) while an active run is in progress (`inRun`) to avoid corrupting procedural map trees.
- **Save/Load & Config Persistence:** Do not serialize illegal modified states into base game save files. Configuration is stored directly in the mod root directory (`config.json`) with automated fallback migrations from `config/default_config.json` and legacy user directories.
- **Deterministic RNG Protection:** Do not mutate shared PRNG streams directly. Isolate tweak overrides from base seed generation.
- **Lifecycle Reset:** Clear all active runtime cheats and transient director overrides (`RuntimeStateManager.ResetSessionState()`) whenever returning to the main menu or starting a fresh run.
- **Async Execution Safety:** When performing card pile operations, hand draws, or combat state mutations, execute safely on the game context (e.g. using `TaskHelper.RunSafely`) to prevent task deadlocks or UI desynchronization.
- **Clean Workspace & Scratch Scripts:** Keep temporary exploration scripts and scratch files (e.g. `.csx` scripts, temporary logs, test dumps) out of Git tracking. Ensure `.gitignore` captures all scratch patterns and clean up temporary files once development tasks conclude.

### 3. File & Component Responsibilities
- `src/Core/Config/`: Strongly typed JSON config (`ModConfig.cs`, `RunSettings.cs`), profile management (`ConfigManager.cs`), and BaseLib config provider (`AIOTweaksBaseLibConfig.cs`).
- `src/Core/Logging/`: Centralized logging wrapper (`ModLogger.cs`) with `[AIOTweaks]` tagging and debug verbosity filtering (supports forced file output in Debug builds).
- `src/Core/State/`: Transient runtime state tracker (`RuntimeStateManager.cs`).
- `src/Hooks/`: Interception points for game systems (Combat, Economy, Events, Map Generation & Neow, Modding & Character Select Screens).
- `src/Cheats/`: Atomic command directors for runtime manipulation (`CardDirector.cs`, `CombatDirector.cs`, `EventDirector.cs`, `InventoryDirector.cs`, `RelicDirector.cs`).
- `src/UI/`: Godot UI components for the draggable/resizable tabbed Mod Settings Dialog (`ModSettingsDialog.cs`), pre-run panel (`PreRunSettingsMenu.cs`), numeric validation (`UIHelper.cs`), and In-Run Debug Console (`DebugConsole.cs`).

---

## Debugging & Diagnostics Workflow (Fast Bug Fixing)

When diagnosing runtime issues, combat softlocks, or mod errors reported during development, inspect the diagnostic outputs automatically generated by debug builds:

### 1. Per-Run Isolated Logs (`logs/runs/aiotweaks_run_<character>_<seed>_<timestamp>.log`)
- **Organized in Folders:** `<ModRootDirectory>/logs/runs/`
- **Only Active in Debug Builds:** Compiled under `#if DEBUG`; in Release mode, compiles to zero-overhead no-ops.
- **Header Metadata & Snapshot Reading:**
  - Game run information: Character name, string seed, numeric seed, profile ID, act room count, and GameMode status (Fair Play vs Custom).
  - Exact generation parameters read from `ActiveRunTweaksSnapshot` (Gold bonus, HP bonus, multipliers, node weights, endless loop counts).
  - Mod settings snapshot from the active profile.
- **Audited Game Values (Post-Hook Verification):**
  - Logs actual pre-hook game values vs post-hook modified values (`[CALC_AUDIT] Pre-Hook: X | Mult: Y | Post-Hook: Z`) for damage and block hooks via `FormulaAuditor`.
  - Verifies that post-hook numbers match actual game calculations, catching calculation bugs and target discrepancies immediately.

### 2. Global Debug Log & Diagnostics
- **Global Log:** `<ModRootDirectory>/aiotweaks_debug.log` auto-flushes every event with microsecond timestamps (`[HH:mm:ss.fff]`).
- **Lag-1 Deduplication Engine & Spam Suppression:**
  - `ModLogger` automatically tracks consecutive identical log events via a Lag-1 filter. Repeating logs within the stream (e.g. per-frame game queries, repetitive hook ticks) are collapsed, suppressing repetitive spam and emitting:
    `↳ [Lag Group] Previous message repeated N times (xM total).`
  - Toggleable live in the in-game Debug Console via the **Lag: ON/OFF** button or typing `lag [on/off/status]`.
- **SQL GROUP BY Analytics:**
  - `ModLogger` maintains a rolling ring buffer of recent log events and supports SQL-style group-by analytics:
    `SELECT COUNT(*), LEVEL, SOURCE, MESSAGE ... GROUP BY LEVEL, SOURCE, MESSAGE ORDER BY COUNT(*) DESC`
  - Run directly in the Debug Console by clicking the **Group By** button or typing `groupby [limit]` (or `sql`).
  - Automatically appended to the bottom of per-run diagnostic logs (`logs/runs/aiotweaks_run_*.log`) upon run session closure.
- **Crash Dumps:** Fatal unhandled exceptions (`AppDomain.CurrentDomain.UnhandledException` and `TaskScheduler.UnobservedTaskException`) captured by `CrashDumpHandler` write timestamped crash files to `<ModRootDirectory>/aiotweaks_crash_YYYYMMDD_HHmmss.txt`.
- **Breadcrumb History:** Ring-buffer trail of recent state events (`BreadcrumbTracker`) captures recent scene transitions, hook executions, card spawns, and combat action steps. Upon any exception in `ModLogger.Error(...)`, the breadcrumb history is automatically attached to the error log.
- **Combat Watchdog Warnings:** `CombatWatchdog` logs actionable warnings when combat is frozen/stalled without state change for >15 seconds.

### 3. Automated Routine Workflow & Build Commands
- **Clean + Debug Build + Deploy:**
  ```bash
  ./build.sh debug --deploy --nuke-logs
  ```
  Nukes all previous `aiotweaks_debug.log*` and `aiotweaks_crash_*.txt` files across source, build temp, user data, and game mod folders, builds a fresh `Debug` assembly (which has forced file logging and diagnostics active), and copies the DLLs straight into the game mod directory.
- **Standalone Log Cleanup:**
  ```bash
  ./build.sh --nuke-logs
  ```
  Deletes accumulated logs/dumps without rebuilding.

### 4. Quick Bug Fixing Checklist for Agents
1. Run `./build.sh debug --deploy --nuke-logs` to ensure a clean slate and active debug diagnostics.
2. Reproduce the bug or error scenario in-game.
3. Check `aiotweaks_debug.log` for recent `[ERROR]` or `[WARN]` logs, hook traces, and exception stack traces.
4. Check for any `aiotweaks_crash_*.txt` crash dumps in the mod folder.
5. Review the breadcrumb sequence preceding the failure to isolate whether the fault originated from a hook prefix/postfix, director call, or UI event.
6. Verify whether the bug reproduces in both `Debug` and `Release` configurations (diagnostics are `#if DEBUG` only).

---

## Technical Constraints & Best Practices
- Use explicit type definitions; avoid ambiguous `dynamic` or loose object casting.
- Gracefully handle invalid IDs (relics, cards, events, enchantments) with actionable warnings in `ModLogger` instead of throwing unhandled exceptions.
- Provide fallback default values for all configuration keys. Hotkeys should default to unassigned (`""`) to prevent keybinding collisions except for standard defaults (`F1` for Console, `F3` for GUI).
- Deploy only the required artifacts: `AIOTweaks.dll` and `AIOTweaks.json` into `mods/AIOTweaks/`.

---

## Project Structure

```text
AiOTweaks_StS2/
├── aiotweaks.sln
├── AIOTweaks.json               # Slay the Spire 2 Mod Manifest
├── build.sh                     # Automated Linux build script
├── README.md                    # User guide and full documentation
├── AGENTS.md                    # Architecture guidelines & agent rules
├── config/
│   └── default_config.json      # Reference default configuration
├── assets/
│   └── icons/                   # UI texture icons & assets
└── src/
    ├── AIOTweaks.csproj         # Godot .NET SDK project targeting net9.0
    ├── Core/
    │   ├── ModEntry.cs          # Mod lifecycle entry point & scene injector
    │   ├── GameHelper.cs        # Reflection, card query & engine utilities
    │   ├── Logging/
    │   │   └── ModLogger.cs     # Centralized [AIOTweaks] logging
    │   ├── Config/
    │   │   ├── ModConfig.cs
    │   │   ├── RunSettings.cs
    │   │   ├── ConfigManager.cs
    │   │   └── AIOTweaksBaseLibConfig.cs
    │   └── State/
    │       └── RuntimeStateManager.cs # Transient state & lifecycle cleanup
    ├── Hooks/
    │   ├── CombatHooks.cs
    │   ├── EconomyHooks.cs
    │   ├── EventHooks.cs
    │   ├── MapGenerationHooks.cs
    │   └── ModdingScreenHooks.cs
    ├── Cheats/
    │   ├── CardDirector.cs
    │   ├── CombatDirector.cs
    │   ├── EventDirector.cs
    │   ├── InventoryDirector.cs
    │   └── RelicDirector.cs
    └── UI/
        ├── UIHelper.cs          # UI extension methods & SpinBox numeric validation
        ├── Menu/
        │   ├── ModSettingsDialog.cs
        │   ├── ModSettingsDialog.tscn
        │   ├── PreRunSettingsMenu.cs
        │   └── PreRunSettingsMenu.tscn
        └── Overlay/
            ├── DebugConsole.cs
            └── DebugConsole.tscn
```