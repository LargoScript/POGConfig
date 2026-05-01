# POGConfig

In-game config UI framework for **Pit of Goblin** mods.

POGConfig provides a shared **MOD SETTINGS** panel that any mod can register entries into. It handles rendering, persistence, scrolling, and input — mod authors only write entry declarations.

**Install:** Thunderstore Mod Manager / r2modman, or place `POGConfig.dll` in the game `Mods` folder. Mods that depend on POGConfig should load after it (MelonLoader respects alphabetical order by default).

---

## Features

- **MODS button** injected into both the main menu and pause menu.
- **Scrollable panel** — mouse wheel and clickable scrollbar. Scrollbar appears only when content overflows.
- **Four entry types:** toggle, slider (with inline numeric text input), options list, keybind.
- **MelonPreferences persistence** — per-entry opt-in, auto-saved on change.
- **Hotkey suppression** — `POGConfig.PanelOpen` lets other mods pause their hotkeys while the panel is open.
- **Marquee animation** — label and value text scroll on hover when they overflow their column.
- **Bidirectional slider fill** — fill bar grows from a configurable origin point, not just the left edge.
- **Step tick marks** — optional evenly-spaced markers on a slider.

---

## For Developers

<details>
<summary><strong>Click To Expand</strong></summary>

### Project reference

Add to your `.csproj`:

```xml
<Reference Include="POGConfig">
  <HintPath>..\..\Mods\POGConfig.dll</HintPath>
  <Private>false</Private>
</Reference>
```

Add `using POGMods.Config;` to your plugin file.

---

### Registration pattern

Wrap registration in a `[MethodImpl(MethodImplOptions.NoInlining)]` helper so the IL2Cpp JIT only resolves POGConfig types when the method is actually called. This makes POGConfig an **optional** dependency — your mod loads cleanly even if POGConfig is absent.

```csharp
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using POGMods.Config;

public override void OnInitializeMelon()
{
    try { TryRegisterConfig(); } catch { }
    MelonLogger.Msg("My Mod loaded.");
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

`Register` also creates a `MelonPreferences` category named after your mod (spaces → underscores). Entries with a `prefKey` load and save automatically within that category.

---

### `POGConfig.PanelOpen`

```csharp
public static bool PanelOpen { get; }
```

`true` while the MOD SETTINGS panel is visible. Check it before processing any in-game hotkey so keypresses inside the panel don't trigger game actions:

```csharp
void Update()
{
    if (!POGConfig.PanelOpen && Input.GetKeyDown(_toggleKey))
        Toggle();
}
```

---

### Entry types

#### `ToggleEntry`

Renders as a 40×24 yellow checkbox on the right side of the row.

```csharp
new ToggleEntry(string label, Func<bool> get, Action<bool> set)
new ToggleEntry(string label, Func<bool> get, Action<bool> set, string prefKey)
```

- The toggle calls `set` immediately when the user clicks it.
- `OnUpdate` polls `get()` every frame and silently syncs the visual state if the value changed externally.
- `prefKey` auto-saves to `MelonPreferences` on every change.

```csharp
new ToggleEntry("God Mode", () => _god, v => _god = v, "GodMode")
```

---

#### `SliderEntry`

Row layout: **26 % label | 26 % value input | 48 % slider**

Label and value columns clip with `RectMask2D` and animate a marquee on hover when the text overflows.

Full constructor (all parameters after `max` are optional):

```csharp
new SliderEntry(
    string label,
    Func<float> get,
    Action<float> set,
    float min,
    float max,
    Func<float, string> fmt = null,   // display formatter; default: v => v.ToString("F1")
    string prefKey          = null,   // MelonPreferences key; null = no persistence
    float  originValue      = 0f,     // where the yellow fill bar anchors from
    bool   showFill         = true,   // false hides the fill bar entirely
    bool   wholeNumbers     = false,  // true snaps the handle to integers
    int    stepPointsCount  = 0)      // ≥2 draws evenly-spaced tick dots along the track
```

**Value input field** — clicking the yellow number area opens an inline text field. The formatter suffix (e.g. `"s"`, `"%"`) is stripped on parse. A regex extracts the first numeric token, so `"43s"` and `"43 seconds"` both parse as `43`. Enter confirms, Escape cancels. Values are clamped to `[min, max]`.

**Bidirectional fill** — the yellow bar spans from `originValue` to the current value. If the current value is below origin it grows left; above origin it grows right. Set `showFill: false` to hide it entirely (recommended for `wholeNumbers` step sliders).

**Step tick marks** — `stepPointsCount` draws that many 4×4 px dots evenly from left to right edge of the track. They are purely visual; combine with `wholeNumbers: true` to make the handle snap between them.

**Examples:**

```csharp
// Simple, 0–100 %, no persistence
new SliderEntry("Volume", () => vol, v => vol = v,
    0f, 1f, v => $"{v * 100:F0}%")

// Bidirectional fill from 0, persisted
new SliderEntry("Temperature", () => temp, v => temp = v,
    -50f, 50f, v => $"{v:F1}°C",
    "Temp", originValue: 0f)

// Integer steps 0–8 with 9 tick dots, no fill, persisted
new SliderEntry("Points", () => (float)pts, v => pts = (int)v,
    0f, 8f, v => $"{v:F0}",
    "Points", originValue: 0f, showFill: false, wholeNumbers: true, stepPointsCount: 9)

// Numeric suffix stripped on parse ("43s" → 43)
new SliderEntry("Backup Interval", () => sec, v => sec = v,
    5f, 180f, v => $"{v:F0}s", "BackupSec", originValue: 5f)
```

---

#### `OptionsSliderEntry`

A slider that snaps to integer indices and shows a string label for each position. No fill bar. Suitable for named modes (difficulty, quality preset, etc.).

```csharp
new OptionsSliderEntry(
    string   label,
    Func<int> get,
    Action<int> set,
    string[] options,
    string   prefKey = null)
```

- `options` is the full list of display strings, index 0 = leftmost.
- The slider's `wholeNumbers` is forced `true` and `maxValue = options.Length - 1`.
- `OnUpdate` polls `get()` and syncs the visual state if the index changed externally.
- `prefKey` saves/loads the integer index.

```csharp
new OptionsSliderEntry(
    "Difficulty",
    () => _difficulty,
    v  => _difficulty = v,
    new[] { "Easy", "Normal", "Hard" },
    "Difficulty")
```

---

#### `KeyEntry`

Row layout: **42 % label | 28 % current key name | 90 px "Change" button**

```csharp
new KeyEntry(string label, Func<KeyCode> get, Action<KeyCode> set)
new KeyEntry(string label, Func<KeyCode> get, Action<KeyCode> set, string prefKey)
```

- Clicking **Change** enters listen mode. The next `Input.GetKeyDown` press is bound. Press Escape to cancel without changing the binding.
- Only one `KeyEntry` can be in listen mode at a time (`KeyEntry.AnyWaiting` flag).
- `prefKey` saves/loads the key name as a string via `Enum.Parse<KeyCode>`.

**`AllowMouseButtons`** — by default `Mouse0`–`Mouse6` are excluded from listen mode (prevents accidentally binding a mouse click). Opt in per-entry:

```csharp
new KeyEntry("Attack", () => _key, v => _key = v) { AllowMouseButtons = true }
```

---

### Persistence details

`MelonPreferences` categories are created as `modName.Replace(" ", "_")` automatically inside `Register`. You do not manage the category yourself. Each entry with a non-null `prefKey`:

| Entry type | Stored as | Loaded via |
|---|---|---|
| `ToggleEntry` | `bool` | direct assignment |
| `SliderEntry` | `float` | clamped to `[min, max]`, rounded if `wholeNumbers` |
| `OptionsSliderEntry` | `int` | clamped to `[0, options.Length - 1]` |
| `KeyEntry` | `string` (key name) | `Enum.TryParse<KeyCode>` |

Saving happens on every value change (inside the wrapped `set` callback). The preferences file is written via `MelonPreferences.Save()`.

---

### Runtime behavior notes

- The `ConfigBehaviour` MonoBehaviour runs on a `DontDestroyOnLoad` runner object with `HideFlags.HideAndDontSave`. The UGUI Canvas lives on a separate root `DontDestroyOnLoad` object — these must not share the same parent to avoid the canvas being hidden by `HideAndDontSave`.
- `BuildStaticUI` is called from `Start` and retried from `Update` on failure. `_uiReady = false` is set on exception, allowing recovery the next frame.
- Content rows are built lazily in `EnsureContent` the first time the panel is opened. If `POGConfig.RegistryVersion` has changed since the last build (a mod registered after the first open), content is destroyed and rebuilt.
- Click detection uses `RectTransformUtility.RectangleContainsScreenPoint(rt, point, null)` — the `null` camera argument is required for `ScreenSpaceOverlay` canvas, because `GetWorldCorners` returns canvas-centered world coordinates while `Input.mousePosition` is bottom-left screen pixels.
- Sliders and toggles use Unity's `EventSystem` pipeline (drag, click). POGConfig creates a `POG_EventSystem` with `StandaloneInputModule` if no `EventSystem` exists in the scene.
- The scrollbar is manually implemented (no `ScrollRect`). `ScrollContent.anchoredPosition.y = _scrollOffset` drives the position; `RectMask2D` on the viewport clips overflow. The thumb height is `VIEWPORT_H² / totalContentH`, clamped to a 30 px minimum.
- `NetworkMenu.TogglePauseMenu` is Harmony-patched: if the panel is open when the game tries to close the pause menu, the patch closes the panel instead and returns `false` to suppress the original call.

---

### Extending with a custom entry type

Subclass `ConfigEntry` and override the internal methods:

```csharp
public class MyEntry : ConfigEntry
{
    public MyEntry(string label) : base(label) { }

    // Build UGUI children inside the provided row GameObject.
    // Row is 40 px tall, full viewport width.
    // Use Clicks.Register(btnRt, callback) for clickable buttons.
    internal override void BuildRowInto(GameObject row, TMP_FontAsset font) { ... }

    // Called every frame while the panel is open.
    internal override void OnUpdate() { ... }

    // Called once during Register() to wire up MelonPreferences.
    internal override void BindPrefs(MelonPreferences_Category cat) { ... }
}
```

</details>
