# AIOTweaks - Slay the Spire 2 Mod

An all-in-one customizable settings, tweak suite, and sandbox debug toolkit for **Slay the Spire 2** built with Godot C# / .NET 8 and HarmonyLib.

---

## Features

### 1. Pre-Run Tweaks & Modifiers
- **Map Node Generation:** Custom multipliers for Elite encounters, Shops, Unknown/Events, Rest sites, and Standard combats without breaking base game PRNG determinism.
- **Fair Play Enforcement:** Modifying any map generation multipliers automatically treats the run as **Seeded / Custom mode** (`GameMode.Custom`), disabling standard progression unlocks and achievements to protect base game integrity. When all map multipliers remain at default `1.0x`, the run proceeds normally as standard.
- **Economy Scaling:** Configurable gold drop multipliers, shop price discounts, and starting gold bonuses.
- **Card Rewards & Stats:** Adjust number of card choices per combat reward and starting max HP.

### 2. In-Run Mod Settings & Debug Controls
- **GUI Menu Overlay (`F3`):** Press `F3` (or your configured GUI overlay key) anytime in-run to open the complete tabbed configuration suite, item spawners, and sandbox tools. You can also open this via BaseLib's in-game Mod Configuration screen.
- **AIOTweaks Console (`F1`):** Press `F1` or `` ` `` (Backquote) anytime in-run to toggle the interactive cheat and debug console.
- **Quick Action Bar:** Instant toggles for:
  - **God Mode** (`F2`): Absorb all incoming player damage.
  - **Infinite Energy**: Energy does not deplete on playing cards.
  - **1-Hit Kill**: Instantly eliminate monsters when dealing damage.
  - **Kill All Enemies** (`F4`): Clear the combat encounter immediately.
  - **+500 Gold / Heal +50 / Draw 3**: One-click instant resource actions.
- **Interactive Command Console:** Full terminal input with auto-scrolling live logs and command history.

### 3. Cheat & Sandbox Directors
- **Relic Director:** Add or remove relics dynamically (`relic <id>`, `rmrelic <id>`).
- **Card Director:** Spawn cards in hand or add upgraded cards into master deck (`card <id> [true/false]`, `handcard <id>`).
- **Inventory Director:** Adjust currency, heal, damage, or set maximum HP (`gold <amount>`, `setgold <amount>`, `heal <amount>`, `damage <amount>`, `setmaxhp <amount>`).
- **Event Director:** Force upcoming narrative events (`event <id>`, `clearevent`).
- **Combat Director:** Modify combat state, draw cards, add energy, or end turns on demand (`draw <count>`, `energy <amount>`, `killall`, `endturn`).

### 4. Non-Destructive Modding Safety
- Safe prefix/postfix patching via **HarmonyLib**.
- Does not corrupt base game save files or serialize illegal runtime overrides.
- In-memory state automatically resets cleanly upon returning to the main menu.

---

## Keybindings (Configurable)

| Key | Action |
| --- | --- |
| `F1` or `` ` `` | Toggle AIOTweaks Debug Console |
| `F2` | Quick Toggle God Mode |
| `F3` | Toggle GUI Menu Overlay & Mod Settings |
| `F4` | Quick Kill All Enemies |

---

## Console Commands

| Command | Description |
| --- | --- |
| `help` | List all available console commands |
| `god` | Toggle God Mode (invulnerability) |
| `infenergy` | Toggle Infinite Energy |
| `onehitkill` / `ohk` | Toggle One-Hit Kill |
| `killall` | Kill all active enemies in combat |
| `endturn` | Instantly end player's turn |
| `gold <amount>` | Add gold to player |
| `setgold <amount>` | Set exact player gold amount |
| `heal <amount>` | Heal player by specified HP |
| `damage <amount>` | Damage player by specified amount |
| `setmaxhp <amount>` | Set player maximum HP |
| `relic <id>` | Add a relic by ID |
| `rmrelic <id>` | Remove a relic by ID |
| `card <id> [upgraded]` | Add a card to master deck (optional upgraded flag) |
| `handcard <id>` | Spawn a card into current combat hand |
| `event <id>` | Force the next rolled event to this ID |
| `clearevent` | Clear active forced event override |
| `draw <count>` | Draw cards immediately in combat |
| `energy <amount>` | Add combat energy |
| `clear` | Clear the console log window |
| `reset` | Reset all active cheats and session overrides |

---

## Project Structure

```text
aiotweaks/
├── AGENTS.md                    # Project guidelines & architectural specification
├── README.md                    # Mod documentation & usage guide
├── aiotweaks.sln                # .NET Solution file
├── src/
│   ├── AIOTweaks.csproj         # C# project targeting net8.0 with Godot & Harmony
│   ├── Core/
│   │   ├── ModEntry.cs          # Mod lifecycle entry point & scene hook
│   │   ├── Logging/
│   │   │   └── ModLogger.cs     # Centralized logger with [AIOTweaks] tagging
│   │   ├── Config/
│   │   │   ├── ModConfig.cs     # Strongly typed config schema
│   │   │   ├── RunSettings.cs   # Per-run profile & modifier settings
│   │   │   └── ConfigManager.cs # Safe JSON persistence & fallbacks
│   │   └── State/
│   │       └── RuntimeStateManager.cs # Transient cheat tracking & lifecycle reset
│   ├── Hooks/
│   │   ├── EconomyHooks.cs      # Gold rewards & shop discount patches
│   │   ├── MapGenerationHooks.cs# Node distribution weight patches
│   │   ├── RelicHooks.cs        # Relic drops & injection patches
│   │   ├── EventHooks.cs        # Event room routing & forcing patches
│   │   └── CombatHooks.cs       # Damage, energy, and draw sandbox patches
│   ├── Cheats/
│   │   ├── RelicDirector.cs     # Atomic relic operations
│   │   ├── EventDirector.cs     # Atomic event manipulation
│   │   ├── InventoryDirector.cs # Currency & HP operations
│   │   ├── CardDirector.cs      # Deck & hand card spawning
│   │   └── CombatDirector.cs    # Combat sandbox & turn management
│   └── UI/
│       ├── Overlay/
│       │   ├── DebugConsole.cs  # In-run CanvasLayer overlay logic
│       │   └── DebugConsole.tscn# Godot scene for overlay
│       └── Menu/
│           ├── PreRunSettingsMenu.cs # Pre-run configuration menu logic
│           └── PreRunSettingsMenu.tscn # Godot scene for pre-run menu
├── assets/
│   └── icons/                   # UI texture assets
└── config/
    └── default_config.json      # Default configuration file
```

---

## Configuration (`config/default_config.json`)

```json
{
  "general": {
    "enabled": true,
    "debugLogging": false,
    "toggleOverlayKey": "F1",
    "quickGodModeKey": "F2",
    "quickKillEnemiesKey": "F3"
  },
  "preRunTweaks": {
    "goldRewardMultiplier": 1.0,
    "shopDiscountMultiplier": 1.0,
    "cardRewardCount": 3,
    "startingGoldBonus": 0,
    "startingMaxHpBonus": 0,
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

## Building from Source

### Prerequisites
- **[.NET 9.0 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)** (or newer)
- **[Godot Engine .NET 4.3+](https://godotengine.org/download)** (Mono / .NET build)
- Git (optional, for cloning)

### CLI Build
1. Open terminal or command prompt in the repository root:
   ```bash
   dotnet restore aiotweaks.sln
   dotnet build aiotweaks.sln -c Release
   ```
2. The output binaries will be generated at:
   ```text
   src/.godot/mono/temp/bin/Release/
   ├── AIOTweaks.dll
   ├── AIOTweaks.json
   ├── 0Harmony.dll
   ├── System.Text.Json.dll
   ├── assets/
   └── config/
   ```

---

## Installation Guide

Follow these simple steps to install **AIOTweaks**:

### Step 1: Get the Mod Files
- **Download:** Download and extract the latest release archive (or build the binaries following the [Building from Source](#building-from-source) section).
- Ensure your `AIOTweaks` folder contains:
  - `AIOTweaks.json` (Required Slay the Spire 2 mod manifest)
  - `AIOTweaks.dll`
  - `0Harmony.dll`
  - `System.Text.Json.dll`
  - `assets/`
  - `config/`

### Step 2: Open the Game's `mods` Directory
1. Open **Steam** and navigate to your Library.
2. Right-click **Slay the Spire 2** → **Manage** → **Browse local files**.
3. Open the `mods` folder (if it doesn't exist, create a folder named `mods`).

<details>
<summary><b>Default Installation Paths</b></summary>

- **Windows:** `C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\mods\AIOTweaks\`
- **Linux:** `~/.local/share/Steam/steamapps/common/Slay the Spire 2/mods/AIOTweaks/`
- **Steam Deck:** `~/.var/app/com.valvesoftware.Steam/.local/share/Steam/steamapps/common/Slay the Spire 2/mods/AIOTweaks/`
</details>

### Step 3: Deploy the Mod
Place the entire `AIOTweaks` folder into the `mods/` directory:

```text
Slay the Spire 2/
└── mods/
    └── AIOTweaks/
        ├── AIOTweaks.json
        ├── AIOTweaks.dll
        ├── 0Harmony.dll
        ├── System.Text.Json.dll
        ├── assets/
        └── config/
```

*(Linux command to deploy directly from local build)*:
```bash
mkdir -p ~/.local/share/Steam/steamapps/common/"Slay the Spire 2"/mods/AIOTweaks
cp AIOTweaks.json assets config ~/.local/share/Steam/steamapps/common/"Slay the Spire 2"/mods/AIOTweaks/ -r
cp src/.godot/mono/temp/bin/Release/*.dll ~/.local/share/Steam/steamapps/common/"Slay the Spire 2"/mods/AIOTweaks/
```

### Step 4: Launch and Play
1. Launch **Slay the Spire 2**.
2. On the Main Menu, click **Mods** and verify **AIOTweaks** is checked and enabled.
3. In-run, open the Pause Menu (`Escape`) and click **"⚙ AIOTweaks Settings"**, or press **`F1`** / **``` ` ```** to toggle the overlay console.

---

### ⚙️ User Configuration Path (Optional)
User settings persist automatically in-game, or can be edited directly at:
- **Windows:** `%APPDATA%\Godot\app_userdata\Slay the Spire 2\AIOTweaks\config.json`
- **Linux / Steam Deck:** `~/.local/share/godot/app_userdata/Slay the Spire 2/AIOTweaks/config.json`

---

## License

MIT License.
