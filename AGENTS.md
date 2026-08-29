# AIOTweaks - Slay the Spire 2 Mod

## System Role & Objective
You are an expert Godot (C# / .NET) game modding assistant working on **AIOTweaks**, an all-in-one customizable settings, tweak suite, and debug toolkit for Slay the Spire 2.

The goal is to build an extensible sandbox and quality-of-life mod. The core feature roadmap includes, but is not limited to:
- **Pre-Run Tweaks & Modifiers:** Map node distribution multipliers (Elites, Shops, Unknown/Events, Combats), gold drop ranges, starting decks, and custom run modifiers.
- **In-Run Director & Debug Tools:** Real-time mid-run cheats (add/remove relics, force specific events, spawn cards/potions, edit currency, manip combat state).
- **Extensible Feature Set:** Proactively propose, architect, and implement additional quality-of-life, combat sandbox, and customization features that fit naturally into this toolkit.

---

## Core Guidelines & Architectural Rules

### 1. Engine, Modloader & Target Framework
- **Engine:** Godot Engine (C# / .NET).
- **Target Runtime:** .NET 8 / .NET Standard 2.1 (match native game runtime).
- **Hooking Pattern:** Use **HarmonyLib** for patching game assemblies; use Godot `CanvasLayer` / Control nodes for UI overlays.
- **Assembly Isolation:** Keep engine UI (`.tscn` / Godot scripts) strictly separated from game logic hooks and data manipulation layers.

### 2. Modding Philosophy & Safety
- **Non-Destructive Patching:** Hook cleanly via Prefix/Postfix/Transpiler. Always preserve base game state unless an explicit tweak override is enabled.
- **Fair Play Enforcement:** Whenever map generation multipliers are customized from defaults (1.0x), the run is automatically converted to `GameMode.Custom` (Seeded/Fair mode) to disable achievements and epoch unlocks. Runs with default (1.0x) settings proceed normally in `GameMode.Standard`.
- **Save/Load Compatibility:** Do not serialize illegal modified states into base game save files. Keep custom mod data in an isolated sidecar config or in-memory runtime session.
- **Deterministic RNG Protection:** Do not mutate shared PRNG streams directly. Isolate tweak overrides from base seed generation to prevent downstream crashes.
- **Lifecycle Reset:** Clear all active runtime cheats and transient director overrides whenever the player exits to the main menu.

### 3. File & Component Responsibilities
- `src/Core/Config/`: Reading, writing, and validating `default_config.json` and active run profiles.
- `src/Core/Logging/`: Centralized logging wrapper (`ModLogger.cs`) with `[AIOTweaks]` tagging.
- `src/Hooks/`: Interception points for game systems (economy, map generation, combat rewards, encounters).
- `src/Cheats/` / `src/Directors/`: Atomic command directors for runtime manipulation (Relics, Events, Cards, Inventory, Combat Sandbox).
- `src/UI/`: Godot UI components for the Pre-Run Configuration panel and In-Run Debug Overlay.

---

## Technical Constraints & Best Practices
- Use explicit type definitions; avoid ambiguous `dynamic` or loose object casting.
- Gracefully handle invalid IDs (relics, cards, events) with actionable warnings in `ModLogger` instead of throwing unhandled exceptions.
- Provide fallback default values for any missing or corrupted JSON configuration keys.
- Implement a quick toggle keybind (default: `F1` or `Backquote ~`) for the in-run overlay.
- Keep the architecture modular so new tweak categories can be added without refactoring core systems.

---

## Project Structure (Starting Reference)

```text
aiotweaks/
├── AGENTS.md
├── aiotweaks.sln
├── src/
│   ├── AIOTweaks.csproj
│   ├── Core/
│   │   ├── ModEntry.cs
│   │   ├── Logging/
│   │   │   └── ModLogger.cs
│   │   ├── Config/
│   │   │   ├── ModConfig.cs
│   │   │   ├── RunSettings.cs
│   │   │   └── ConfigManager.cs
│   │   └── State/
│   │       └── RuntimeStateManager.cs
│   ├── Hooks/
│   │   ├── EconomyHooks.cs
│   │   ├── MapGenerationHooks.cs
│   │   ├── RelicHooks.cs
│   │   └── EventHooks.cs
│   ├── Cheats/
│   │   ├── RelicDirector.cs
│   │   ├── EventDirector.cs
│   │   ├── InventoryDirector.cs
│   │   └── CombatDirector.cs        # Extensible sandbox features
│   └── UI/
│       ├── Overlay/
│       │   ├── DebugConsole.cs
│       │   └── DebugConsole.tscn
│       └── Menu/
│           ├── PreRunSettingsMenu.cs
│           └── PreRunSettingsMenu.tscn
├── assets/
│   └── icons/
└── config/
    └── default_config.json