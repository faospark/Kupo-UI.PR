# KupoUI.PR

A BepInEx IL2CPP plugin scaffold for Final Fantasy Pixel Remaster (FF1-FF6).

This plugin includes runtime patches for UI cleanup and custom texture replacement across Final Fantasy Pixel Remaster (FF1-FF6).

## Features

- BepInEx IL2CPP plugin structure (`BasePlugin`)
- Harmony runtime patching (`PatchAll`)
- Soft dependency detection for:
  - `Memoria.FFPR`
  - `Magicite`
- Cross-game-safe first patch using Unity API (`UnityEngine.Cursor`)
- Layered custom texture system with general and category pack folders

## Project Layout

- `KupoUI.PR.csproj` - .NET Framework 4.7.2 class library project
- `src/KupoUIPRPlugin.cs` - plugin entry point
- `src/Patches/DisableMouseCursorPatch.cs` - first runtime patch
- `src/Patches/CustomTexturePatch.cs` - sprite/UI texture replacement patches
- `src/Compatibility/ExternalModDetector.cs` - optional mod detection helper
- `src/Textures/TextureResolver.cs` - texture indexing/loading/replacement core
- `src/Textures/GameTagDetector.cs` - FF1-FF6 auto detection helper

## Build Requirements

1. Install BepInEx IL2CPP (6.0-pre.2 or newer) into your FFPR game folder.
2. Ensure interop assemblies are generated (`BepInEx/interop`).
3. Build with `BepInExDir` set to that game's BepInEx folder.

Example:

```powershell
dotnet build .\KupoUI.PR.csproj -c Release -p:BepInExDir="D:\Games\FINAL FANTASY II PR\BepInEx"
```

## Install

Copy output DLL from:

- `bin/Release/net472/KupoUI.PR.dll`

to:

- `BepInEx/plugins/KupoUI.PR/`

## Config

Generated on first run in `BepInEx/config`:

- `General.DisableMouseCursor` (default: `true`)
- `UI.SaveHighlightColor` (default: `DarkNavy`; options: `Original`, `DarkNavy`, `DarkGreen`, `DarkViolet`, `DarkYellow`, `DarkOrange`, `Disable`)
- `Textures.EnableCustomTextures` (default: `true`)
- Texture root folder is fixed to `<GameRoot>/Modules/`
- `Textures.EnableTextureHotReload` (default: `true`)
- `Textures.TextureHotReloadDebounceMs` (default: `350`)
- `Textures.EnableDDSTextures` (default: `true`)
- `Textures.UIFramesFolder` (default: `Default`; selects a folder under `02-UI-Frames`)
- `Textures.UIBgColorFolder` (default: `Default`; selects a folder under `03-UI-BgColor`)
- `Textures.CursorsFolder` (default: `Default`; selects a folder under `04-UI-Cursors`)
- `Textures.ButtonPromptsFolder` (default: `Default`; selects a folder under `05-Button-Prompts`)
- `Textures.TextureLogger` (default: `Discoveries,Resolutions`; options: `All`, `None`, or comma-separated categories: `Discoveries`, `Resolutions`, `Misses`)

Set to `false` to disable this initial patch while keeping the plugin active.

## Custom Texture Folder Layout

Default root:

- `<GameRoot>/Modules/`

Recommended folders created automatically:

- `00-Mods/` (general shared overrides)
- `02-UI-Frames/Default/`
- `03-UI-BgColor/Default/`
- `04-UI-Cursors/Default/`
- `05-Button-Prompts/Default/`

Pack selection behavior:

- `Textures.UIFramesFolder` selects `02-UI-Frames/<value>/`
- `Textures.UIBgColorFolder` selects `03-UI-BgColor/<value>/`
- `Textures.CursorsFolder` selects `04-UI-Cursors/<value>/`
- `Textures.ButtonPromptsFolder` selects `05-Button-Prompts/<value>/`
- `Default` means no special pack selected; only files you place in those folders apply.

Lookup priority (highest to lowest):

1. `05-Button-Prompts/<SelectedPack>`
2. `04-UI-Cursors/<SelectedPack>`
3. `03-UI-BgColor/<SelectedPack>`
4. `02-UI-Frames/<SelectedPack>`
5. `00-Mods/`
6. Legacy compatibility folders (`Shared`, `FF1`..`FF6`, `00-Mods/Shared`, `00-Mods/FF1`..`FF6`) if present

Use file names without extension to match in-game texture/sprite names (for example `window_frame.png` to replace `window_frame`).

### Path-based overrides (recommended for collisions)

Many FFPR assets reuse the same file names in different bundles. To avoid collisions, KupoUI.PR also supports path-based resolution for files placed under a `GameAssets` folder.

How it works:

- If a replacement file path contains a `GameAssets/...` segment, the resolver indexes it by full relative path (without extension), not only by file name.
- At runtime, when the game loads an address like `Assets/GameAssets/...`, KupoUI.PR can resolve to the exact matching replacement path first.
- Name-only matching still works as a fallback for existing setups.

Example (FF2 portrait):

- In-game address: `Assets/GameAssets/Serial/Res/Chara/Face/FA_FF2_P001/Default_00.png`
- Replacement file location:
  - `<GameRoot>/Modules/00-Mods/GameAssets/Serial/Res/Chara/Face/FA_FF2_P001/Default_00.png`

This allows `Default_00.png` files from different portrait folders/bundles to be replaced independently.

### Optional sidecar metadata

You can add a JSON file next to a replacement texture with the same base name to override any combination of logical size, filtering, pivot, 9-slice border, and source rect. All fields are optional — only include what you need to override.

Example:

- `Default_00.png`
- `Default_00.json`

Example JSON:

```json
{
  "width": 112,
  "height": 144,
  "pixelsPerUnit": 100,
  "filterMode": "Point",
  "wrapMode": "Clamp",
  "pivot": "0.5,0.5",
  "border": "4,4,4,4",
  "rectX": 0,
  "rectY": 16
}
```

> **Note:** All fields are optional. Only include the ones relevant to your replacement.

Supported fields:

- `width`: logical source width used when calculating replacement sprite scale
- `height`: logical source height used when calculating replacement sprite scale
- `pixelsPerUnit`: optional direct sprite PPU override (takes priority over auto scale calculation)
- `filterMode`: Unity-style filter override, one of `Point`, `Bilinear`, or `Trilinear`
- `filterType`: alias for `filterMode` (same accepted values)
- `pointFilter`: legacy boolean shorthand, `true` = `Point`, `false` = `Bilinear`
- `wrapMode`: Unity-style wrap mode override, one of `Clamp`, `Repeat`, `Mirror`, or `MirrorOnce` (default: `Clamp`)
- `pivot`: normalized sprite anchor point as `"x,y"` (each value 0–1). Examples: `"0.5,0.5"` = center, `"0,0"` = bottom-left, `"0.5,0"` = bottom-center. Overrides the original sprite's pivot. Values are clamped to 0–1.
- `border`: 9-slice border in pixels as `"left,bottom,right,top"`. Example: `"4,4,4,4"`. Overrides the original sprite's border; use `"0,0,0,0"` to strip an inherited border.
- `rectX`: pixel X offset within the replacement texture to start sampling from. When omitted, inherits from the original sprite's rect x position. Clamped to valid texture bounds. Note: this controls **source UV position** inside the replacement image, not the sprite's position on screen.
- `rectY`: pixel Y offset within the replacement texture to start sampling from. Same rules as `rectX`. Useful when a single replacement image contains multiple sprites at known offsets.

If both `filterMode`/`filterType` and `pointFilter` are present, the string mode takes priority.

If `width` and/or `height` are provided, sprite creation uses those values to override replacement rect sizing when possible; when values do not fit atlas coordinates, fallback uses origin-clamped sizing.

Supported texture formats:

- `png`, `jpg`, `jpeg`, `tga`
- `dds` (DXT1, DXT5, and uncompressed 32-bit RGBA DDS)

Filter mode behavior:

- Default behavior is Bilinear filtering.
- Point filtering is applied automatically if the replacement file is inside a folder named `Pixel` or `Pixels` (at any depth).
- For path-based `GameAssets/...` overrides, prefer sidecar `filterMode` metadata when the folder convention is not practical.

Hot-reload notes:

- With hot-reload enabled, changes in the texture folders trigger an automatic reindex.
- Reindexing is debounced by `TextureHotReloadDebounceMs` to avoid repeated rebuilds while copying many files.

Texture logger notes:

- Discovery logs report unique texture names seen from sprite/texture hooks.
- Resolution logs report unique names that successfully map to replacement files.
- Miss logs are optional and can be noisy; keep disabled unless diagnosing missing replacements.

## Notes On Optional Dependencies

This plugin does not hard-reference `Memoria.FFPR` or `Magicite` yet.

At runtime, it checks loaded assemblies and logs whether those mods are present, enabling future integration paths without breaking standalone execution.

---

## ObjectConfig.json — Data-driven GameObject Tweaks

You can manipulate Unity GameObjects at runtime (position, rotation, scale, active state) without writing any C# — just drop an `ObjectConfig.json` file inside any mod folder under `Modules/00-Mods/`.

The plugin scans **all** `ObjectConfig.json` files found recursively under `Modules/00-Mods/` when the game starts.

### Folder placement

```
<GameRoot>/
  Modules/
    00-Mods/
      MyMod/
        ObjectConfig.json   ← picked up automatically
      AnotherMod/
        ObjectConfig.json   ← also picked up
```

### File format

```json
{
  "objects": [
    {
      "TargetObjectName": "menu_base(Clone)",
      "TargetPath": "Canvas/aspect_parent/menu_parent/menu_base(Clone)",
      "SceneName": "Title",
      "Position": { "x": 0, "y": -50, "z": 0 },
      "Rotation": { "x": 0, "y": 0,   "z": 0 },
      "Scale":    { "x": 0.9, "y": 0.9, "z": 1.0 },
      "SetActive": true,
      "TextAlignment": "MiddleCenter",
      "FontSize": 24,
      "ResizeTextForBestFit": true,
      "ResizeTextMaxSize": 36,
      "ResizeTextMinSize": 12,
      "TextColorWhite": true,
      "DisableShadow": true
    }
  ]
}
```

The `objects` array can contain as many entries as you need, across one file or spread across multiple files in different mod folders.

### Fields

| Field | Required | Description |
|---|---|---|
| `TargetObjectName` | **Yes** | Exact `GameObject` name to match (e.g. `"menu_base(Clone)"`). |
| `TargetPath` | No | Hierarchy path suffix to disambiguate objects with the same name. Forward-slash notation, matched from the object upward. E.g. `"Canvas/aspect_parent/menu_base(Clone)"`. |
| `SceneName` | No | Only apply this rule while this scene is active. Omit to apply in every scene. Case-insensitive. |
| `Position` | No | Sets `transform.localPosition`. Provide `x`, `y`, `z` as floats. |
| `Rotation` | No | Sets `transform.localEulerAngles` (Euler angles in degrees). Provide `x`, `y`, `z`. |
| `Scale` | No | Sets `transform.localScale`. Provide `x`, `y`, `z`. |
| `SetActive` | No | Calls `gameObject.SetActive(value)`. Use `true` or `false`. |
| `TextAlignment` | No | Sets `Text.alignment` on the `UnityEngine.UI.Text` component (if present). See [text alignment values](#text-alignment-values) below. |
| `FontSize` | No | Sets `Text.fontSize` on the `UnityEngine.UI.Text` component (if present). Provide an integer. |
| `ResizeTextForBestFit` | No | Sets `Text.resizeTextForBestFit` on the `UnityEngine.UI.Text` component (if present). Use `true` or `false`. |
| `ResizeTextMaxSize` | No | Sets `Text.resizeTextMaxSize` on the `UnityEngine.UI.Text` component (if present). Provide an integer. |
| `ResizeTextMinSize` | No | Sets `Text.resizeTextMinSize` on the `UnityEngine.UI.Text` component (if present). Provide an integer. |
| `TextColorWhite` | No | Forces `Text.color` to `Color.white` on the `UnityEngine.UI.Text` component (if present). Re-enforced on every color write to prevent the game from overriding it. Use `true`. |
| `DisableShadow` | No | Disables all `UnityEngine.UI.Shadow` components on the matching GameObject. Use `true`. |

> **Note:** All fields except `TargetObjectName` are optional. Only include the ones you want to change — unspecified fields leave the object unchanged.

### When rules are applied

Rules fire at two moments:

1. **Scene load** — when a scene finishes loading, all GameObjects in the scene are scanned and matching rules are applied. This covers objects that are already active from the start.
2. **SetActive(true)** — when any GameObject is enabled at runtime, matching rules are applied immediately. This covers UI panels and objects toggled on after load.

### Using `TargetPath` to avoid wrong matches

If multiple objects share the same name (common in FFPR), add `TargetPath` to target only the one you want:

```json
{
  "TargetObjectName": "menu_base(Clone)",
  "TargetPath": "RootObject/Canvas/aspect_parent/menu_parent/menu_base(Clone)",
  "Scale": { "x": 0.9, "y": 0.9, "z": 1.0 }
}
```

The path is matched by walking up the transform hierarchy from the object, so it does not need to start from the scene root — a suffix is enough.

> **Important:** Two common mistakes to avoid:
> - The **last segment of `TargetPath` must match `TargetObjectName`** exactly. The matcher walks upward from the object itself, so the object's own name must appear at the end of the path.
> - **No trailing slash.** A path ending with `/` produces an empty final segment that will never match any GameObject name, causing the rule to silently do nothing.

### Hiding an object

```json
{
  "TargetObjectName": "some_ui_element",
  "SceneName": "MainMenu",
  "SetActive": false
}
```

> **Note on `SetActive: false` behaviour:** The rule uses a Harmony prefix that intercepts every `SetActive(true)` call and flips it to `false` before Unity processes it. This permanently prevents the object from becoming active — no flicker, no one-frame delay.

### Changing text alignment

If the target object has a `UnityEngine.UI.Text` component, you can set its horizontal and vertical alignment:

```json
{
  "TargetObjectName": "some_label",
  "TargetPath": "Canvas/panel/some_label",
  "TextAlignment": "MiddleCenter"
}
```

#### Text alignment values

| Value | Description |
|---|---|
| `UpperLeft` | Top-left corner |
| `UpperCenter` | Top-center |
| `UpperRight` | Top-right corner |
| `MiddleLeft` | Vertically centered, left-aligned |
| `MiddleCenter` | Fully centered |
| `MiddleRight` | Vertically centered, right-aligned |
| `LowerLeft` | Bottom-left corner |
| `LowerCenter` | Bottom-center |
| `LowerRight` | Bottom-right corner |

Values are case-insensitive. If the object has no `Text` component, or the value is unrecognized, a warning is written to the BepInEx log and the rule is skipped.

### Combining multiple rules

```json
{
  "objects": [
    {
      "TargetObjectName": "ui_root",
      "TargetPath": "RootObject/sab_canvas/root/ui_root",
      "Scale": { "x": 0.9, "y": 0.9, "z": 1.0 }
    },
    {
      "TargetObjectName": "title_logo",
      "SceneName": "Title",
      "Position": { "x": 0, "y": 80, "z": 0 }
    }
  ]
}
```

---

## Title Screen Full Background Image (`TitlescreenFullBG`)

You can inject a custom full-screen background image on the title screen by placing a texture file named `TitlescreenFullBG` in any mod folder. No config entry is required — if the file is absent nothing happens.

### How it works

The patch watches for the title screen's internal `background` object at:

```
background_canvas/ui_root/backgrou_root/background
```

When that object activates, a new `fullbg` GameObject is injected as a sibling immediately above it in the hierarchy:

```
background_canvas/ui_root/backgrou_root/
  ├── background   ← original solid-color background (unchanged)
  └── fullbg       ← injected — renders on top of background
```

`fullbg` is a `RawImage` stretched to fill its parent, so it completely covers `background`. The solid-color background underneath is still tinted by `UI-Title-Screen.TitleScreenBgColor` if configured — `fullbg` simply covers it.

### Installation

Drop any supported image file named `TitlescreenFullBG` into any mod folder:
- With hot-reload enabled, changes in the texture folders trigger an automatic reindex.
- Reindexing is debounced by `TextureHotReloadDebounceMs` to avoid repeated rebuilds while copying many files.

Texture logger notes:

- Discovery logs report unique texture names seen from sprite/texture hooks.
- Resolution logs report unique names that successfully map to replacement files.
- Miss logs are optional and can be noisy; keep disabled unless diagnosing missing replacements.

## Notes On Optional Dependencies

This plugin does not hard-reference `Memoria.FFPR` or `Magicite` yet.

At runtime, it checks loaded assemblies and logs whether those mods are present, enabling future integration paths without breaking standalone execution.

---

## ObjectConfig.json — Data-driven GameObject Tweaks

You can manipulate Unity GameObjects at runtime (position, rotation, scale, active state) without writing any C# — just drop an `ObjectConfig.json` file inside any mod folder under `Modules/00-Mods/`.

The plugin scans **all** `ObjectConfig.json` files found recursively under `Modules/00-Mods/` when the game starts.

### Folder placement

```
<GameRoot>/
  Modules/
    00-Mods/
      MyMod/
        ObjectConfig.json   ← picked up automatically
      AnotherMod/
        ObjectConfig.json   ← also picked up
```

### File format

```json
{
  "objects": [
    {
      "TargetObjectName": "menu_base(Clone)",
      "TargetPath": "Canvas/aspect_parent/menu_parent/menu_base(Clone)",
      "SceneName": "Title",
      "Position": { "x": 0, "y": -50, "z": 0 },
      "Rotation": { "x": 0, "y": 0,   "z": 0 },
      "Scale":    { "x": 0.9, "y": 0.9, "z": 1.0 },
      "SetActive": true,
      "TextAlignment": "MiddleCenter",
      "FontSize": 24,
      "ResizeTextForBestFit": true,
      "ResizeTextMaxSize": 36,
      "ResizeTextMinSize": 12,
      "TextColorWhite": true,
      "DisableShadow": true
    }
  ]
}
```

The `objects` array can contain as many entries as you need, across one file or spread across multiple files in different mod folders.

### Fields

| Field | Required | Description |
|---|---|---|
| `TargetObjectName` | **Yes** | Exact `GameObject` name to match (e.g. `"menu_base(Clone)"`). |
| `TargetPath` | No | Hierarchy path suffix to disambiguate objects with the same name. Forward-slash notation, matched from the object upward. E.g. `"Canvas/aspect_parent/menu_base(Clone)"`. |
| `SceneName` | No | Only apply this rule while this scene is active. Omit to apply in every scene. Case-insensitive. |
| `Position` | No | Sets `transform.localPosition`. Provide `x`, `y`, `z` as floats. |
| `Rotation` | No | Sets `transform.localEulerAngles` (Euler angles in degrees). Provide `x`, `y`, `z`. |
| `Scale` | No | Sets `transform.localScale`. Provide `x`, `y`, `z`. |
| `SetActive` | No | Calls `gameObject.SetActive(value)`. Use `true` or `false`. |
| `TextAlignment` | No | Sets `Text.alignment` on the `UnityEngine.UI.Text` component (if present). See [text alignment values](#text-alignment-values) below. |
| `FontSize` | No | Sets `Text.fontSize` on the `UnityEngine.UI.Text` component (if present). Provide an integer. |
| `ResizeTextForBestFit` | No | Sets `Text.resizeTextForBestFit` on the `UnityEngine.UI.Text` component (if present). Use `true` or `false`. |
| `ResizeTextMaxSize` | No | Sets `Text.resizeTextMaxSize` on the `UnityEngine.UI.Text` component (if present). Provide an integer. |
| `ResizeTextMinSize` | No | Sets `Text.resizeTextMinSize` on the `UnityEngine.UI.Text` component (if present). Provide an integer. |
| `TextColorWhite` | No | Forces `Text.color` to `Color.white` on the `UnityEngine.UI.Text` component (if present). Re-enforced on every color write to prevent the game from overriding it. Use `true`. |
| `DisableShadow` | No | Disables all `UnityEngine.UI.Shadow` components on the matching GameObject. Use `true`. |

> **Note:** All fields except `TargetObjectName` are optional. Only include the ones you want to change — unspecified fields leave the object unchanged.

### When rules are applied

Rules fire at two moments:

1. **Scene load** — when a scene finishes loading, all GameObjects in the scene are scanned and matching rules are applied. This covers objects that are already active from the start.
2. **SetActive(true)** — when any GameObject is enabled at runtime, matching rules are applied immediately. This covers UI panels and objects toggled on after load.

### Using `TargetPath` to avoid wrong matches

If multiple objects share the same name (common in FFPR), add `TargetPath` to target only the one you want:

```json
{
  "TargetObjectName": "menu_base(Clone)",
  "TargetPath": "RootObject/Canvas/aspect_parent/menu_parent/menu_base(Clone)",
  "Scale": { "x": 0.9, "y": 0.9, "z": 1.0 }
}
```

The path is matched by walking up the transform hierarchy from the object, so it does not need to start from the scene root — a suffix is enough.

> **Important:** Two common mistakes to avoid:
> - The **last segment of `TargetPath` must match `TargetObjectName`** exactly. The matcher walks upward from the object itself, so the object's own name must appear at the end of the path.
> - **No trailing slash.** A path ending with `/` produces an empty final segment that will never match any GameObject name, causing the rule to silently do nothing.

### Hiding an object

```json
{
  "TargetObjectName": "some_ui_element",
  "SceneName": "MainMenu",
  "SetActive": false
}
```

> **Note on `SetActive: false` behaviour:** The rule uses a Harmony prefix that intercepts every `SetActive(true)` call and flips it to `false` before Unity processes it. This permanently prevents the object from becoming active — no flicker, no one-frame delay.

### Changing text alignment

If the target object has a `UnityEngine.UI.Text` component, you can set its horizontal and vertical alignment:

```json
{
  "TargetObjectName": "some_label",
  "TargetPath": "Canvas/panel/some_label",
  "TextAlignment": "MiddleCenter"
}
```

#### Text alignment values

| Value | Description |
|---|---|
| `UpperLeft` | Top-left corner |
| `UpperCenter` | Top-center |
| `UpperRight` | Top-right corner |
| `MiddleLeft` | Vertically centered, left-aligned |
| `MiddleCenter` | Fully centered |
| `MiddleRight` | Vertically centered, right-aligned |
| `LowerLeft` | Bottom-left corner |
| `LowerCenter` | Bottom-center |
| `LowerRight` | Bottom-right corner |

Values are case-insensitive. If the object has no `Text` component, or the value is unrecognized, a warning is written to the BepInEx log and the rule is skipped.

### Combining multiple rules

```json
{
  "objects": [
    {
      "TargetObjectName": "ui_root",
      "TargetPath": "RootObject/sab_canvas/root/ui_root",
      "Scale": { "x": 0.9, "y": 0.9, "z": 1.0 }
    },
    {
      "TargetObjectName": "title_logo",
      "SceneName": "Title",
      "Position": { "x": 0, "y": 80, "z": 0 }
    }
  ]
}
```

---

## Title Screen Full Background Image (`TitlescreenFullBG`)

You can inject a custom full-screen background image on the title screen by placing a texture file named `TitlescreenFullBG` in any mod folder. No config entry is required — if the file is absent nothing happens.

### How it works

The patch watches for the title screen's internal `background` object at:

```
background_canvas/ui_root/backgrou_root/background
```

When that object activates, a new `fullbg` GameObject is injected as a sibling immediately above it in the hierarchy:

```
background_canvas/ui_root/backgrou_root/
  ├── background   ← original solid-color background (unchanged)
  └── fullbg       ← injected — renders on top of background
```

`fullbg` is a `RawImage` stretched to fill its parent, so it completely covers `background`. The solid-color background underneath is still tinted by `UI-Title-Screen.TitleScreenBgColor` if configured — `fullbg` simply covers it.

### Installation

Drop any supported image file named `TitlescreenFullBG` into any mod folder:

```
<GameRoot>/Modules/00-Mods/MyMod/TitlescreenFullBG.png
```

Supported formats: `png`, `jpg`, `jpeg`, `tga`, `dds`.

### Notes

- If no `TitlescreenFullBG` file is found in the index, the patch is a complete no-op — no object is created, no log entries are written.
- The object is only created once per activation cycle. Re-activating the title screen background does not create duplicates.
- The image is stretched to fill its parent rect. For best results, use an image sized to your target resolution (e.g. 1920×1080).
- The texture is kept alive with `DontDestroyOnLoad` so it survives any additive scene reloads on the title screen.


## Font Diagnostic & Custom Font Swap

This plugin includes a two-phase font mapping and replacement utility to swap game fonts with custom `.ttf` or `.otf` font files cleanly.

### Phase 1 — Diagnostic Logging (Always-On by Default)

When the game initializes fonts, details about the font parameters are printed to the BepInEx console and log files. 

- **Log File Location:** `<GameRoot>/BepInEx/LogOutput.log`
- **What to look for:** Look for lines starting with `[FontMap]`. For example:
  ```
  [Info   :KupoUI.PR] [FontMap] FontType=Font09 | Language=En | FontName=PIXELREMASTERFONT.ttf | LineSpace=0.66 | Font=
  ```
This mapping shows exactly which `FontType` enum corresponds to which language and default asset file in the game.

Configuration for Phase 1 (in `faospark.kupoui.pr.cfg` under `BepInEx/config`):
- `Diagnostics.LogFontMapping` (bool, default `true`): Set to `false` to disable diagnostic logging.

### Phase 2 — Custom Font File Swap (Off by Default)

You can place your custom font files (TrueType `.ttf` or OpenType `.otf`) inside the mod fonts directory and map them granularly using a configuration file.

#### 1. Folder & Config Location
All custom font files and configuration are placed under:
- **Directory:** `<GameRoot>/Modules/00-Mods/Fonts/`
- **Configuration File:** `<GameRoot>/Modules/00-Mods/Fonts/fontconfig.json`

*(Note: On first startup, the mod will automatically create the `Fonts/` folder and generate a default template `fontconfig.json` file if they are missing.)*

#### 2. Configuration File Format (`fontconfig.json`)
The mapping file supports both **simple string mappings** (where the font family name defaults to the filename without extension) and **object-based mappings** (highly recommended for custom fonts to ensure the exact font family name and sizing is passed to the engine):

```json
{
  "// Instructions": "Map a FontType enum name (Font01..Font10, Default) to a font file in 00-Mods/Fonts/. Specify FontFile and FontName (family name) to ensure characters render correctly.",
  "Font01": {
    "FontFile": "HARNGTON.TTF",
    "FontName": "Harrington",
    "LineSpace": 1.0
  },
  "Font02": {
    "FontFile": "my_arial_substitute.otf",
    "FontName": "MyArialSubstitute",
    "LineSpace": 1.25,
    "FontSize": 32
  },
  "Font09": {
    "FontFile": "my_pixel_font.ttf",
    "FontName": "MyPixelFont"
  },
  "Default": "HARNGTON.TTF"
}
```

*   **`FontFile`**: The filename of the `.ttf` or `.otf` file located directly in the `00-Mods/Fonts/` directory.
*   **`FontName`**: **(Highly Recommended)** The font family name (e.g. `"Harrington"`, `"Segoe UI"`). This is registered with the OS GDI system and resolved by Unity's dynamic font engine, preventing blank text rendering.
*   **`LineSpace`**: A decimal factor representing the line height/spacing (e.g. `1.2`). Override this if your replacement font appears too cramped or overflows dialogue boxes vertically.
*   **`FontSize`**: An integer representing the target rendering size (e.g. `32`). If omitted, it will automatically scale to match the size of the default font it replaces.

#### 3. Enabling the Swap
Once you have configured your `fontconfig.json` and placed your font files:
1. Open `<GameRoot>/BepInEx/config/faospark.kupoui.pr.cfg`.
2. Set **`FontSwap.Enabled`** to `true`.
3. Restart the game.

*Custom fonts are cached in memory upon first load, ensuring there is zero performance impact or stutter during scene transitions.*
