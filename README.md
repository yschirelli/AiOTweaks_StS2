# AIOTweaks — Slay the Spire 2 Mod & Sandbox Suite

[![Game](https://img.shields.io/badge/Game-Slay%20the%20Spire%202-red.svg)](https://store.steampowered.com/app/2868840/Slay_the_Spire_2/)
[![Framework](https://img.shields.io/badge/Engine-Godot%204.3%20%28Mono%29-blue.svg)](https://godotengine.org/)
[![Target](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![Patching](https://img.shields.io/badge/Patching-HarmonyX-orange.svg)](https://github.com/BepInEx/HarmonyX)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**AIOTweaks** (All-in-One Tweaks) is an advanced Quality of Life (QoL), modding API sandbox, and real-time debugging suite designed specifically for **Slay the Spire 2** (built on Godot 4.3 Mono / C# .NET 9). 

Whether you are testing new card synergies, prototyping custom balance patches, exploring procedural map generation, or experimenting with sandbox cheats, AIOTweaks gives you full runtime control over game systems through non-destructive Harmony hooks, an in-game HUD console, and a tabbed configuration GUI.

---

## Table of Contents
- [Features](#features)
- [Keybindings & Controls](#keybindings--controls)
- [Console Commands](#console-commands)
- [Project Architecture](#project-architecture)
- [Required Dependencies](#required-dependencies)
- [Building from Source](#building-from-source)
- [Installation Guide](#installation-guide)
- [Configuration](#configuration)
- [Troubleshooting](#troubleshooting)
- [License](#license)

---

## Features

### In-Game Debug Console & Cheat Engine
- Toggleable overlay console with auto-scroll, command history (Up/Down navigation), and color-coded status output.
- Instant godmode, infinite energy, one-hit kills, full enemy sweep clears, custom card/relic spawning, and direct combat manipulation.
- Safe lifecycle reset: Transient cheats and overrides automatically reset when exiting runs or returning to the main menu.

### Tabbed Mod Settings GUI & Pre-Run Tweaks
- Open the dedicated **Mod Settings Dialog** at any time (via in-game Mods screen, Character Select screen, or user-configured hotkey). Accessible during combat and active runs!
- **Run-Lock Safety**: Pre-run generation parameters (Map Room Count, Starting Bonuses, Node Weights, Neow Bonus) are safely locked while a run is active to prevent illegal mid-run map mutation.
- Configure run modifiers: starting gold bonus, starting max HP bonus, gold reward multipliers, shop discount percentages, and spawn Neow at start.
- Custom card reward draft counts (expand beyond standard 3 choices).
- Standalone **Enemy Health Multiplier**, **Enemy Damage Multiplier**, and **Enemy Defend Multiplier** (scales enemy block proportionally).

### Endless Mode with Compounding Enemy Scaling
- Configurable Endless Mode loop reset scaling: $\text{Effective Multiplier} = \text{NormalMultiplier} \times (\text{EndlessMultiplier})^{\text{LoopCount}}$.
- Dynamically scales enemy HP, incoming damage, and block values across infinite loops.

### Map Generation, Size & Free Navigation
- Customizable map floor/room length (15 to 30 rooms).
- Customizable node distribution weights: fine-tune the frequency of Elites, Shops, Unknown/Events, Rest Sites, and Normal Combats.
- **Free Map Navigation ("Flying Boots" mode)**: Click and travel to ANY room freely on the map, omni-directionally without pathing restrictions.
- Pre-run map generation and starting bonus settings are locked during active runs and fully editable in the Main Menu.

### Non-Destructive Hook Architecture & BaseLib Integration
- Built with **HarmonyX** prefix and postfix patches across core game assemblies.
- Patches fail open and wrap all reflection queries in safe try-catch blocks to prevent game crashes.
- Seamlessly registers with **BaseLib**'s `ModConfigRegistry` for native menu integration.

---

## Keybindings & Controls

| Action | Config Key | Default Hotkey |
| :--- | :--- | :--- |
| Toggle AIOTweaks Debug Console | `consoleHotkey` | `F1` |
| Quick Toggle God Mode (Invulnerability) | `quickGodModeKey` | *Unassigned* |
| Toggle Tabbed Mod Settings & Sandbox GUI | `guiOverlayHotkey` | `F3` |
| Quick Kill All Active Enemies | `quickKillEnemiesKey` | *Unassigned* |

Hotkeys can be customized at any time in the **Mod Settings Dialog** or in `config.json`.


---

## Console Commands

Open the console with your configured keybind and execute any of the following commands:

| Command | Syntax / Example | Description |
| :--- | :--- | :--- |
| `help` | `help` | Lists all registered console commands with syntax. |
| `god` | `god` | Toggles God Mode (player becomes immune to all incoming damage). |
| `infenergy` | `infenergy` | Toggles Infinite Energy in combat. |
| `onehitkill` / `ohk` | `ohk` | Toggles One-Hit Kill (all player attacks deal fatal damage). |
| `killall` | `killall` | Instantly defeats all active enemies in combat. |
| `endturn` | `endturn` | Instantly forces the player's turn to conclude. |
| `gold` | `gold 500` | Adds the specified amount of gold to the player inventory. |
| `setgold` | `setgold 999` | Sets player gold to an exact value. |
| `heal` | `heal 50` | Restores the specified amount of HP. |
| `damage` | `damage 20` | Deals direct damage to the player. |
| `setmaxhp` | `setmaxhp 120` | Modifies the player's maximum HP. |
| `relic` | `relic BurningBlood` | Adds a relic to the player by ID or class name. |
| `rmrelic` | `rmrelic BurningBlood` | Removes an active relic from the player. |
| `card` | `card Strike_R true` | Adds a card to the master deck (optional `true`/`false` for upgraded). |
| `handcard` | `handcard Defend_R` | Spawns a card directly into current combat hand. |
| `draw` | `draw 3` | Immediately draws specified number of cards in combat. |
| `energy` | `energy 2` | Adds specified energy points in combat. |
| `event` | `event BigFish` | Forces the next rolled unknown room to be a specific event ID. |
| `clearevent` | `clearevent` | Clears active forced event overrides. |
| `clear` | `clear` | Clears text from the console log window. |
| `reset` | `reset` | Clears all transient cheats and resets state to default. |

---

## Project Architecture

```text
AiOTweaks_StS2/
├── aiotweaks.sln                # .NET Solution file
├── AIOTweaks.json               # Slay the Spire 2 Mod Manifest
├── build.sh                     # Automated Linux build & assembly discovery script
├── README.md                    # Documentation & user guide
├── AGENTS.md                    # Architecture guidelines & agent rules
├── config/
│   └── default_config.json      # Default schema and fallback reference
├── assets/
│   └── icons/                   # UI texture icons & assets
│       └── README.md
└── src/
    ├── AIOTweaks.csproj         # C# project targeting net9.0 + Godot Mono
    ├── Core/
    │   ├── ModEntry.cs          # Mod lifecycle entry point & scene injector
    │   ├── GameHelper.cs        # Reflection & engine helper utilities
    │   ├── Logging/
    │   │   └── ModLogger.cs     # Centralized [AIOTweaks] logging
    │   ├── Config/              # Strongly typed JSON config & profile manager
    │   │   ├── ModConfig.cs
    │   │   ├── RunSettings.cs
    │   │   ├── ConfigManager.cs
    │   │   └── AIOTweaksBaseLibConfig.cs
    │   └── State/
    │       └── RuntimeStateManager.cs # Transient cheat tracking & lifecycle resets
    ├── Hooks/                   # Harmony patching modules
    │   ├── CharacterSelectHooks.cs # Character select screen config button injection
    │   ├── CombatHooks.cs       # Invulnerability, energy, damage, and draw
    │   ├── EconomyHooks.cs      # Gold reward & shop price patches
    │   ├── EventHooks.cs        # Event manipulation & forcing
    │   ├── MapGenerationHooks.cs# Node weight & map generation patches
    │   ├── ModdingScreenHooks.cs# In-game Modding screen config button injection
    │   ├── NeowHooks.cs         # Neow / blessing manipulation
    │   └── RelicHooks.cs        # Relic pool & drop manipulation
    ├── Cheats/                  # Domain-specific cheat managers
    │   ├── CardDirector.cs      # Deck and hand card spawning
    │   ├── CombatDirector.cs    # Combat sandbox & turn management
    │   ├── EventDirector.cs     # Event routing and queue forcing
    │   ├── InventoryDirector.cs # Currency, HP, and potion operations
    │   └── RelicDirector.cs     # Atomic relic injection/removal
    └── UI/                      # Godot scene overlays and controls
        ├── Menu/
        │   ├── ModSettingsDialog.cs   # Tabbed Mod Settings & Sandbox Dialog
        │   ├── ModSettingsDialog.tscn
        │   ├── PreRunSettingsMenu.cs  # Character select pre-run tweaks panel
        │   └── PreRunSettingsMenu.tscn
        └── Overlay/
            ├── DebugConsole.cs  # In-run CanvasLayer Debug Console
            └── DebugConsole.tscn
```

---

## Required Dependencies

- **[.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)** (or later)
- **[Godot Engine 4.3+ (.NET / Mono Build)](https://godotengine.org/download)**
- **Slay the Spire 2** (Installed via Steam)
- NuGet packages (resolved automatically at compile-time):
  - `Lib.Harmony` (>= 2.3.3)

---

## Building from Source

### Quick Build (Linux / Steam Deck)
Run the root build script, which automatically detects your .NET SDK, locates Steam game assemblies across standard directories, compiles the Release binary, and displays the output location:
```bash
./build.sh
```

### Manual Build
1. Clone or open the repository:
   ```bash
   git clone https://github.com/yschirelli/AiOTweaks_StS2.git
   cd AiOTweaks_StS2
   ```

2. Restore dependencies and compile the release binary:
   ```bash
   dotnet restore aiotweaks.sln
   dotnet build aiotweaks.sln -c Release
   ```

3. The compiled assembly and manifest will be output to:
   ```text
   src/.godot/mono/temp/bin/Release/
   ├── AIOTweaks.dll
   └── AIOTweaks.json
   ```

---

## Installation Guide

1. Obtain or build `AIOTweaks.dll` and `AIOTweaks.json`.
2. Locate your Slay the Spire 2 `mods` folder (create it in the game root if it does not exist):
   - **Windows:** `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\AIOTweaks\`
   - **Linux / Steam Deck:** `~/.local/share/Steam/steamapps/common/Slay the Spire 2/mods/AIOTweaks/`
3. Copy `AIOTweaks.dll` and `AIOTweaks.json` into the `mods/AIOTweaks/` directory.
4. Launch the game.

---

## Configuration

Settings persist per-user in Godot's application user data directory:
- **Windows:** `%APPDATA%\Godot\app_userdata\Slay the Spire 2\AIOTweaks\config.json`
- **Linux / Steam Deck:** `~/.local/share/godot/app_userdata/Slay the Spire 2/AIOTweaks/config.json`

```json
{
  "general": {
    "enabled": true,
    "debugLogging": false,
    "consoleHotkey": "F1",
    "guiOverlayHotkey": "F3",
    "quickGodModeKey": "",
    "quickKillEnemiesKey": ""
  },
  "preRunTweaks": {
    "goldRewardMultiplier": 1.0,
    "shopDiscountMultiplier": 1.0,
    "cardRewardCount": 3,
    "startingGoldBonus": 0,
    "startingMaxHpBonus": 0,
    "forceNeowBonus": true,
    "mapRoomCount": 15,
    "enemyHealthMultiplier": 1.0,
    "enemyDamageMultiplier": 1.0,
    "enemyDefendMultiplier": 1.0,
    "freeMapNavigation": false,
    "endlessMode": {
      "enabled": false,
      "enemyScalingMultiplier": 2.0
    },
    "mapNodeDistribution": {
      "eliteWeightMultiplier": 1.0,
      "shopWeightMultiplier": 1.0,
      "eventWeightMultiplier": 1.0,
      "restSiteWeightMultiplier": 1.0,
      "combatWeightMultiplier": 1.0
    }
  },
  "combatSandbox": {
    "godMode": false,
    "infiniteEnergy": false,
    "oneHitKill": false,
    "bonusDrawPerTurn": 0,
    "maxHandSizeOverride": 10,
    "infinitePotions": false,
    "noCardExhaust": false
  },
  "ui": {
    "overlayScale": 1.0,
    "overlayOpacity": 0.95,
    "showDebugConsoleOnStart": false,
    "enableAudioCues": true
  }
}
```

---

## Troubleshooting

- **Opening Mod Settings or Console:** Press `F3` for Mod Settings or `F1` for Debug Console. You can also access settings directly from the in-game **Mods** menu or customize keybindings in `config.json`.
- **Mod not showing up in Mods list:** Verify `AIOTweaks.json` is located directly in `mods/AIOTweaks/AIOTweaks.json` alongside `AIOTweaks.dll`.
- **Build error with missing Godot assemblies:** Ensure the Godot .NET SDK / targeting packs are installed and the .NET 9 SDK is active (`dotnet --version`).

---

## License

This project is licensed under the [MIT License](LICENSE).
