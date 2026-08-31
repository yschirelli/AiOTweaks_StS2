# AIOTweaks — Slay the Spire 2 Mod & Sandbox Suite

[![Game](https://img.shields.io/badge/Game-Slay%20the%20Spire%202-red.svg)](https://store.steampowered.com/app/2868840/Slay_the_Spire_2/)
[![Framework](https://img.shields.io/badge/Engine-Godot%204.3%20%28Mono%29-blue.svg)](https://godotengine.org/)
[![Target](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![Patching](https://img.shields.io/badge/Patching-HarmonyX-orange.svg)](https://github.com/BepInEx/HarmonyX)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)

**AIOTweaks** (All-in-One Tweaks) is an advanced Quality of Life (QoL), modding API sandbox, and real-time debugging suite designed specifically for **Slay the Spire 2** (built on Godot 4.3 Mono / C# .NET 9). 

Whether you are testing new card synergies, prototyping custom balance patches, exploring procedural map generation, or experimenting with sandbox cheats, AIOTweaks gives you full runtime control over game systems through non-destructive Harmony hooks, an in-game HUD console, and a draggable/resizable tabbed configuration GUI.

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
- Toggleable overlay console (`F1` by default) with auto-scroll, command history (Up/Down navigation), quick-action buttons, and color-coded status output.
- Instant god mode, infinite energy, one-hit kills, full enemy sweep clears, max energy adjustment, player damage multiplier scaling, and direct turn progression.
- Live card, relic, potion, and event directors with instant runtime spawning, removal, and query inspection.
- Safe lifecycle reset: Transient cheats and overrides automatically reset when exiting runs or returning to the main menu.

### Draggable & Resizable Mod Settings GUI
- Open the dedicated **Mod Settings Dialog** at any time via `F3` (or custom hotkey), the in-game **Mods** screen, or the **Character Select** screen. Accessible during combat and active runs!
- **Movable & Resizable Layout**: Drag the window from the header bar, resize freely via the bottom-right grip, or reset anytime to default window layout with persistent window coordinates saved automatically.
- **Interactive Hotkey Assignment**: Click "Assign Key" on any hotkey row to instantly bind keys (supports Esc cancellation and Backspace/Delete to clear).
- **Run-Lock Safety**: Pre-run generation parameters (Map Room Count, Starting Bonuses, Node Weights, Neow Bonus) are safely locked while a run is active to prevent illegal mid-run map mutation.
- Configure run modifiers: starting gold bonus, starting max HP bonus, gold reward multipliers, shop discount percentages, card reward draft counts, and spawn Neow toggle.
- Standalone **Enemy Health Multiplier**, **Enemy Damage Multiplier**, **Enemy Defend Multiplier** (scales enemy block proportionally), and **Player Damage Multiplier**.
- **Dynamic Max Energy Control**: Adjust baseline max energy with real-time UI synchronization and combat state tracking.

### Deck, Hand & Pile Manipulation Suite
- Real-time tabbed views for **Current Master Deck**, **Combat Hand**, **Draw Pile**, **Discard Pile**, and **Exhaust Pile** with live card counts and keyword badges.
- Instant card addition/removal, manual draw to hand, force exhaust, upgrade/downgrade toggling.
- **Keyword & Attribute Editor**: Toggle attributes dynamically on any card (Ethereal, Exhaust, Innate, Eternal, Unplayable, Retain, etc.).
- **Enchantment Director**: Apply or clear custom card enchantments with configurable multiplier/amount parameters.
- **Relic Director**: Search, equip, stack, and remove any relic in the game with rarity color coding and live counter display.

### Random Merchant Shop Anywhere
- Open a freshly randomized merchant shop room anywhere during an active run (even during combat!) via dedicated button, `shop` console command, or configurable quick hotkey (`quickOpenShopKey`).
- Buy cards, relics, potions, or purge cards on the fly without breaking run state. Closeable seamlessly via Proceed button or Escape key.

### Endless Mode with Compounding Enemy Scaling
- Configurable Endless Mode loop reset scaling: $\text{Effective Multiplier} = \text{NormalMultiplier} \times (\text{EndlessMultiplier})^{\text{LoopCount}}$.
- Dynamically scales enemy HP, incoming damage, and block values across infinite loops.

### Map Generation, Node Weights & Free Navigation
- Customizable map floor/room length (15 to 50 rooms) with auto-scaling background parchment tiling and boss scroll limit recalculation.
- Customizable node distribution weights: fine-tune the frequency of Elites, Shops, Unknown/Events, Rest Sites, Normal Combats, and Treasure Rooms.
- **Free Map Navigation ("Flying Boots" mode)**: Click and travel to ANY room freely on the map, omni-directionally without pathing restrictions.
- **Fair Play Safety**: Non-default map node weights automatically mark runs as Seeded/Custom to safeguard standard achievements and epoch unlocks.

### Non-Destructive Hook Architecture & BaseLib Integration
- Built with **HarmonyX** prefix and postfix patches across core game assemblies.
- Patches fail open, ensure RoomSet Ancient/Boss fallbacks, and wrap all reflection queries in safe try-catch blocks to prevent game crashes.
- Seamlessly registers with **BaseLib**'s `ModConfigRegistry` for native menu integration.

---

## Keybindings & Controls

| Action | Config Key | Default Hotkey | Notes |
| :--- | :--- | :--- | :--- |
| Toggle AIOTweaks Debug Console | `consoleHotkey` | `F1` | Interactive terminal overlay |
| Toggle Tabbed Mod Settings & Sandbox GUI | `guiOverlayHotkey` | `F3` | Draggable & resizable settings dialog |
| Quick Toggle God Mode (Invulnerability) | `quickGodModeKey` | *Unassigned* | Instant combat god mode toggle |
| Quick Kill All Active Enemies | `quickKillEnemiesKey` | *Unassigned* | Defeats all active enemies with death animations |
| Quick Open Shop Anywhere | `quickOpenShopKey` | *Unassigned* | Opens randomized shop overlay mid-run |

> [!TIP]
> Hotkeys can be interactively assigned in the **Mod Settings Dialog** (`Tweaks & Multipliers` tab), configured in `config.json`, or via BaseLib mod config menu. Default hotkeys (`F1` and `F3`) are automatically restored if left blank.

---

## Console Commands

Open the console with your configured keybind (`F1`) and execute any of the following commands:

| Command | Syntax / Example | Description |
| :--- | :--- | :--- |
| `help` | `help` | Lists all registered console commands with syntax. |
| `god` | `god` | Toggles God Mode (player becomes immune to all incoming damage). |
| `infenergy` | `infenergy` | Toggles Infinite Energy in combat. |
| `onehitkill` / `ohk` | `ohk` | Toggles One-Hit Kill (all player attacks deal fatal damage). |
| `killall` | `killall` | Instantly defeats all active enemies in combat. |
| `endturn` | `endturn` | Instantly forces the player's turn to conclude. |
| `gold` | `gold 500` | Adds the specified amount of gold to player inventory. |
| `setgold` | `setgold 999` | Sets player gold to an exact value. |
| `heal` | `heal 50` | Restores the specified amount of HP. |
| `damage` / `dmg` | `damage 20` | Deals direct damage to the player. |
| `setmaxhp` | `setmaxhp 120` | Modifies the player's maximum HP. |
| `relic` | `relic BurningBlood` | Adds a relic to the player by ID or class name. |
| `rmrelic` | `rmrelic BurningBlood` | Removes an active relic from the player. |
| `card` | `card Strike_R true` | Adds a card to the master deck (optional `true`/`false` for upgraded). |
| `handcard` | `handcard Defend_R` | Spawns a card directly into current combat hand. |
| `rmcard` | `rmcard Strike_R` | Removes a card instance from master deck. |
| `upcard` | `upcard Strike_R` | Toggles upgrade status of a card in master deck. |
| `exhaust` | `exhaust Strike_R` | Exhausts a card from current hand/deck to exhaust pile. |
| `enchant` | `enchant Strike_R Swift 2` | Applies an enchantment with specified amount/multiplier. |
| `disenchant` | `disenchant Strike_R` | Clears all enchantments from specified card. |
| `attr` / `keyword` | `attr Strike_R Ethereal add` | Adds, removes, or toggles card keyword attributes. |
| `event` | `event BigFish` | Forces the next rolled unknown room to be a specific event ID. |
| `clearevent` | `clearevent` | Clears active forced event overrides. |
| `shop` / `openshop` | `shop` | Opens a randomized merchant shop overlay anywhere. |
| `draw` | `draw 3` | Immediately draws specified number of cards in combat. |
| `energy` | `energy 2` | Adds specified energy points in combat. |
| `maxenergy` | `maxenergy 5` | Sets or displays the baseline Max Energy count. |
| `playerdmg` / `dmgmult` | `playerdmg 2.5` | Sets or displays the player damage multiplier. |
| `verbose` / `debuglog` | `verbose on` | Toggles verbose diagnostic logging. |
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
    │   ├── GameHelper.cs        # Reflection, card/relic query & engine utilities
    │   ├── Logging/
    │   │   └── ModLogger.cs     # Centralized [AIOTweaks] logging & file diagnostics
    │   ├── Config/              # Strongly typed JSON config & profile manager
    │   │   ├── ModConfig.cs     # Configuration schema definitions
    │   │   ├── RunSettings.cs   # Run profile settings schema
    │   │   ├── ConfigManager.cs # Config file persistence & fallback loader
    │   │   └── AIOTweaksBaseLibConfig.cs # BaseLib ModConfigRegistry provider
    │   └── State/
    │       └── RuntimeStateManager.cs # Transient cheat tracking & lifecycle resets
    ├── Hooks/                   # Harmony patching modules
    │   ├── CharacterSelectHooks.cs # Character select screen config button injection
    │   ├── CombatHooks.cs       # Invulnerability, energy, damage, def & draw hooks
    │   ├── EconomyHooks.cs      # Gold reward & shop price patches
    │   ├── EventHooks.cs        # Event manipulation & forcing
    │   ├── MapGenerationHooks.cs# Node weight, map length & free navigation patches
    │   ├── ModdingScreenHooks.cs# In-game Modding screen config button injection
    │   └── NeowHooks.cs         # Neow / blessing manipulation & Ancient fallbacks
    ├── Cheats/                  # Domain-specific cheat managers
    │   ├── CardDirector.cs      # Deck, hand, pile, attribute & enchantment spawning
    │   ├── CombatDirector.cs    # Combat sandbox & turn management
    │   ├── EventDirector.cs     # Event routing and queue forcing
    │   ├── InventoryDirector.cs # Currency, HP, and potion operations
    │   └── RelicDirector.cs     # Atomic relic injection/removal
    └── UI/                      # Godot scene overlays and controls
        ├── UIHelper.cs          # SpinBox numeric validation & UI helper extensions
        ├── Menu/
        │   ├── ModSettingsDialog.cs   # Draggable/resizable Tabbed Mod Settings Dialog
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
Run the root build script, which automatically detects your .NET SDK, locates Steam game assemblies across standard directories, compiles the binary, and displays the output location:

```bash
# Compile Release build (default)
./build.sh

# Compile Debug build (forcefully enables verbose logging and writes to aiotweaks_debug.log in mod root)
./build.sh debug
```

### Manual Build
1. Clone or open the repository:
   ```bash
   git clone https://github.com/yschirelli/AiOTweaks_StS2.git
   cd AiOTweaks_StS2
   ```

2. Restore dependencies and compile the binary:
   ```bash
   # Release build
   dotnet restore aiotweaks.sln
   dotnet build aiotweaks.sln -c Release

   # Debug build (with forced verbose logging and file logger)
   dotnet build aiotweaks.sln -c Debug
   ```

3. The compiled assembly and manifest will be output to:
   ```text
   src/.godot/mono/temp/bin/Release/  (or bin/Debug/)
   ├── AIOTweaks.dll
   ├── AIOTweaks.pdb
   └── AIOTweaks.json
   ```

> [!NOTE]
> **Debug Builds**: When built in `Debug` configuration, verbose logging is forcefully enabled by default regardless of config file settings, and all real-time diagnostics are written to `aiotweaks_debug.log` directly in the mod's root folder.

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

Settings persist directly inside the mod root directory (`mods/AIOTweaks/config.json`) so your preferences and window layouts persist across sessions:

```json
{
  "general": {
    "enabled": true,
    "debugLogging": false,
    "consoleHotkey": "F1",
    "guiOverlayHotkey": "F3",
    "quickGodModeKey": "",
    "quickKillEnemiesKey": "",
    "quickOpenShopKey": ""
  },
  "preRunTweaks": {
    "goldRewardMultiplier": 1.0,
    "shopDiscountMultiplier": 1.0,
    "cardRewardCount": 3,
    "startingGoldBonus": 0,
    "startingMaxHpBonus": 0,
    "forceNeowBonus": true,
    "mapRoomCount": 15,
    "playerDamageMultiplier": 1.0,
    "maxEnergy": 3,
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
      "combatWeightMultiplier": 1.0,
      "treasureRoomMultiplier": 1.0
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
    "enableAudioCues": true,
    "menuPosX": null,
    "menuPosY": null,
    "menuWidth": null,
    "menuHeight": null
  }
}
```

---

## Troubleshooting

- **Opening Mod Settings or Console:** Press `F3` for Mod Settings or `F1` for Debug Console. You can also access settings directly from the in-game **Mods** menu, **Character Select** screen, or customize keybindings in `config.json`.
- **Mod not showing up in Mods list:** Verify `AIOTweaks.json` is located directly in `mods/AIOTweaks/AIOTweaks.json` alongside `AIOTweaks.dll`.
- **Resetting Window Size / Layout:** If the settings dialog was resized or moved off-screen, click the "Reset to Game Defaults" button inside the dialog or delete the `menuPosX`/`menuPosY`/`menuWidth`/`menuHeight` keys in `config.json`.
- **Build error with missing Godot assemblies:** Ensure the Godot .NET SDK / targeting packs are installed and the .NET 9 SDK is active (`dotnet --version`).

---

## License

This project is licensed under the [MIT License](LICENSE).
