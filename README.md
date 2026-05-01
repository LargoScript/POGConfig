# POGConfig

In-game config UI framework for **Pit of Goblin** mods.

POGConfig provides a shared MOD SETTINGS panel that any mod can register entries into. It handles rendering, persistence, scrolling, and input — mod authors only write the entry declarations.

**Install:** Thunderstore Mod Manager / r2modman, or place `POGConfig.dll` in the game `Mods` folder. Other mods that depend on POGConfig must load after it.

---

## Features

- MOD SETTINGS button added to the main menu and pause menu.
- Scrollable panel with mouse wheel and scrollbar support.
- Entry types: toggle, slider (with numeric text input), options list, keybind.
- Persistent settings via MelonPreferences (per mod, per entry key).
- Hotkeys in other mods are suppressed while the panel is open.

---

## For Developers

<details>
<summary><strong>Click To Expand</strong></summary>

### Dependency setup

Reference `POGConfig.dll` in your `.csproj`:

```xml
<Reference Include="POGConfig">
  <HintPath>..\POGConfig\bin\Release\net6.0\POGConfig.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Add `using POGMods.Config;` to your plugin file.

---

### Registering entries

Call `POGConfig.Register` once, inside a `[MethodImpl(MethodImplOptions.NoInlining)]` helper to keep the dependency optional:

```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using POGMods.Config;

public override void OnInitializeMelon()
{
    try { TryRegisterConfig(); } catch { }
}

[MethodImpl(MethodImplOptions.NoInlining)]
private static void TryRegisterConfig()
{
    POGConfig.Register("My Mod", new List<ConfigEntry>
    {
        new ToggleEntry("Enable Feature", () => _enabled, v => _enabled = v),
        new SliderEntry("Speed",          () => _speed,   v => _speed   = v, 0f, 10f),
        new KeyEntry("Hotkey",            () => _key,     v => _key     = v),
    });
}
```

The `try/catch` wrapper means your mod loads even when POGConfig is absent.

---

### Entry types

#### `ToggleEntry`

```csharp
new ToggleEntry(string label, Func<bool> get, Action<bool> set)
new ToggleEntry(string label, Func<bool> get, Action<bool> set, string prefKey)
```

Renders as a yellow check box. `prefKey` auto-saves the value to MelonPreferences under your mod's category.

```csharp
new ToggleEntry("God Mode", () => _god, v => _god = v, "GodMode")
```

---

#### `SliderEntry`

Full signature:

```csharp
new SliderEntry(
    string label,
    Func<float> get,
    Action<float> set,
    float min,
    float max,
    Func<float, string> fmt       = null,   // display formatter, default: "F1"
    string prefKey                = null,   // auto-save key
    float  originValue            = 0f,     // where the yellow fill bar starts
    bool   showFill               = true,   // show/hide the fill bar
    bool   wholeNumbers           = false,  // snap to integers
    int    stepPointsCount        = 0)      // number of evenly-spaced step markers (0 = disabled)
```

The value area is a **click-to-type text field** — click it to type a number, press Enter to confirm, Escape to cancel. Suffix characters in the formatter (e.g. `"s"`, `"%"`) are stripped automatically when parsing.

**Examples:**

Simple:
```csharp
new SliderEntry("Volume", () => vol, v => vol = v, 0f, 1f, x => $"{x * 100:F0}%")
```

Bidirectional fill (fill grows left or right from 0):
```csharp
new SliderEntry("Temperature", () => temp, v => temp = v,
    -50f, 50f, v => $"{v:F1}°C",
    "Temp", originValue: 0f, showFill: true)
```

Whole-number step slider with 9 tick marks (no fill bar):
```csharp
new SliderEntry("Points", () => pts, v => pts = v,
    0f, 8f, v => $"{v:F0} pts",
    "Points", wholeNumbers: true, showFill: false, stepPointsCount: 9)
```

Duration with suffix:
```csharp
new SliderEntry("Backup Interval", () => sec, v => sec = v,
    5f, 180f, v => $"{v:F0}s", "BackupSec", originValue: 5f)
```

---

#### `OptionsSliderEntry`

Discrete string options snapped evenly across a slider. No fill bar. The current option name is shown in the value area.

```csharp
new OptionsSliderEntry(
    string label,
    Func<int> get,
    Action<int> set,
    string[] options,
    string prefKey = null)
```

Example:
```csharp
new OptionsSliderEntry(
    "Difficulty",
    () => _difficulty,
    v => _difficulty = v,
    new[] { "Easy", "Normal", "Hard" },
    "Difficulty")
```

---

#### `KeyEntry`

```csharp
new KeyEntry(string label, Func<KeyCode> get, Action<KeyCode> set)
new KeyEntry(string label, Func<KeyCode> get, Action<KeyCode> set, string prefKey)
```

Shows the current key name and a **Change** button. Clicking Change enters listen mode — press any key to bind it. Press Escape to cancel.

By default mouse buttons (`Mouse0`–`Mouse6`) are blocked from being bound. Opt in per-entry:

```csharp
new KeyEntry("Attack", () => _key, v => _key = v) { AllowMouseButtons = true }
```

---

### Suppressing hotkeys while the panel is open

Check `POGConfig.PanelOpen` before processing any in-game hotkey:

```csharp
void Update()
{
    if (!POGConfig.PanelOpen && Input.GetKeyDown(_toggleKey))
    {
        // apply toggle
    }
}
```

---

### Persistence with `prefKey`

Each entry type accepts an optional `prefKey`. When provided, the value is automatically saved to and loaded from MelonPreferences under a category named after your mod (spaces replaced with underscores). You do **not** need to manage `MelonPreferences_Category` yourself.

---

### Best practices

- Wrap `TryRegisterConfig` in `[MethodImpl(MethodImplOptions.NoInlining)]` so the IL2Cpp JIT only resolves the POGConfig types when the method is actually called — this prevents crashes when POGConfig is absent.
- Keep callbacks lightweight. Callbacks fire on every value change; avoid scene scanning or heavy allocations inside them.
- Always check `POGConfig.PanelOpen` before reading hotkeys in `Update`.
- Use `prefKey` for any setting that should persist across sessions.

</details>
