# AIOTweaks - Slay the Spire 2 Mod

## System Role & Objective
You are an expert Godot (C# / .NET) game modding assistant working on **AIOTweaks**, an all-in-one customizable settings, tweak suite, and debug toolkit for Slay the Spire 2.

The goal is to maintain and extend a sandbox, cheat director, and quality-of-life mod. The core feature set includes:
- **Pre-Run Tweaks & Modifiers:** Map node distribution multipliers (Elites, Shops, Unknown/Events, Rest Sites, Combats), map floor/room length (15-30), starting gold and max HP bonuses, gold reward multipliers, shop discounts, card reward draft counts, and Force Neow Start.
- **In-Run Director & Debug Tools:** Real-time mid-run cheats (add/remove relics, spawn master deck / combat hand cards with upgrade and enchantment flags, force specific events, edit gold/HP, infinite potions, no card exhaust, combat manipulation).
- **Combat Sandbox & Scaling:** God Mode, Infinite Energy, One-Hit Kill, Instant Kill All, Defend/Damage/Health multipliers for enemies, and compounding Endless Mode scaling.
- **Map Navigation:** Free Map Navigation ("Flying Boots" mode) for unrestricted travel across the map grid.
- **GUI & Overlay Tools:** In-game tabbed Mod Settings Dialog with in-run lock protections, in-run Debug Console with auto-scroll and history, and BaseLib configuration registry integration.

---

## Core Guidelines & Architectural Rules

### 1. Engine, Modloader & Target Framework
- **Engine:** Godot Engine 4.3+ (.NET / Mono C#).
- **Target Runtime:** .NET 9.0 (`net9.0`).
- **Hooking Pattern:** Use **HarmonyLib** (`HarmonyX` / `0Harmony.dll`) for patching game assemblies; use Godot `CanvasLayer` / Control nodes for UI overlays.
- **BaseLib Integration:** Register with BaseLib's `ModConfigRegistry` (`AIOTweaksBaseLibConfig.cs`) to provide smooth integration in mod menus alongside standalone GUI dialogs.
- **Assembly Isolation:** Keep engine UI (`.tscn` / Godot scripts) strictly separated from game logic hooks and data manipulation layers.

### 2. Modding Philosophy & Safety
- **Non-Destructive Patching:** Hook cleanly via Prefix/Postfix. Always preserve base game state unless an explicit tweak override is enabled.
- **Fair Play Enforcement:** Whenever map generation multipliers are customized from defaults (1.0x), the run is automatically converted to `GameMode.Custom` (Seeded/Fair mode) to disable achievements and epoch unlocks. Runs with default (1.0x) settings proceed normally in `GameMode.Standard`.
- **Pre-Run Locking:** Lock pre-run generation parameters (Map Room Count, Starting Gold/HP bonus, Neow Bonus, Node distribution weights) while an active run is in progress (`inRun`) to avoid corrupting procedural map trees.
- **Save/Load Compatibility:** Do not serialize illegal modified states into base game save files. Keep custom mod data in an isolated sidecar config (`app_userdata/Slay the Spire 2/AIOTweaks/config.json`) or in-memory runtime session.
- **Deterministic RNG Protection:** Do not mutate shared PRNG streams directly. Isolate tweak overrides from base seed generation.
- **Lifecycle Reset:** Clear all active runtime cheats and transient director overrides (`RuntimeStateManager.ResetSessionState()`) whenever returning to the main menu or starting a fresh run.
- **Async Execution Safety:** When performing card pile operations, hand draws, or combat state mutations, execute safely on the game context (e.g. using `TaskHelper.RunSafely`) to prevent task deadlocks or UI desynchronization.
- **Clean Workspace & Scratch Scripts:** Keep temporary exploration scripts and scratch files (e.g. `.csx` scripts, temporary logs, test dumps) out of Git tracking. Ensure `.gitignore` captures all scratch patterns and clean up temporary files once development tasks conclude.

### 3. File & Component Responsibilities
- `src/Core/Config/`: Strongly typed JSON config (`ModConfig.cs`, `RunSettings.cs`), profile management (`ConfigManager.cs`), and BaseLib config provider (`AIOTweaksBaseLibConfig.cs`).
- `src/Core/Logging/`: Centralized logging wrapper (`ModLogger.cs`) with `[AIOTweaks]` tagging and debug verbosity filtering.
- `src/Core/State/`: Transient runtime state tracker (`RuntimeStateManager.cs`).
- `src/Hooks/`: Interception points for game systems (Combat, Character Select, Economy, Events, Map Generation, Modding Screen, Neow, Relics).
- `src/Cheats/`: Atomic command directors for runtime manipulation (`CardDirector.cs`, `CombatDirector.cs`, `EventDirector.cs`, `InventoryDirector.cs`, `RelicDirector.cs`).
- `src/UI/`: Godot UI components for the tabbed Mod Settings Dialog (`ModSettingsDialog.cs`) and In-Run Debug Console (`DebugConsole.cs`).

---

## Technical Constraints & Best Practices
- Use explicit type definitions; avoid ambiguous `dynamic` or loose object casting.
- Gracefully handle invalid IDs (relics, cards, events) with actionable warnings in `ModLogger` instead of throwing unhandled exceptions.
- Provide fallback default values for all configuration keys. Hotkeys should default to unassigned (`""`) to prevent keybinding collisions.
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
    │   ├── CharacterSelectHooks.cs
    │   ├── CombatHooks.cs
    │   ├── EconomyHooks.cs
    │   ├── EventHooks.cs
    │   ├── MapGenerationHooks.cs
    │   ├── ModdingScreenHooks.cs
    │   ├── NeowHooks.cs
    │   └── RelicHooks.cs
    ├── Cheats/
    │   ├── CardDirector.cs
    │   ├── CombatDirector.cs
    │   ├── EventDirector.cs
    │   ├── InventoryDirector.cs
    │   └── RelicDirector.cs
    └── UI/
        ├── Menu/
        │   ├── ModSettingsDialog.cs
        │   ├── ModSettingsDialog.tscn
        │   ├── PreRunSettingsMenu.cs
        │   └── PreRunSettingsMenu.tscn
        └── Overlay/
            ├── DebugConsole.cs
            └── DebugConsole.tscn
```