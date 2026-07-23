# KupoUI.PR

A BepInEx IL2CPP plugin for Final Fantasy Pixel Remaster (FF1–FF6) that provides runtime UI patches, custom texture replacement, dialogue enhancements, and data-driven GameObject tweaks.

---

## Table of Contents

- [Features](#features)
- [Build Requirements](#build-requirements)
- [Install](#install)
- [Configuration Reference](#configuration-reference)
- [Custom Texture System](#custom-texture-system)
  - [Folder Layout](#folder-layout)
  - [Lookup Priority](#lookup-priority)
  - [Path-Based Overrides](#path-based-overrides)
  - [Sidecar Metadata (.json)](#sidecar-metadata-json)
  - [Texture Formats & Filter Modes](#texture-formats--filter-modes)
  - [Hot-Reload](#hot-reload)
  - [Texture Logger](#texture-logger)
- [ObjectConfig.json — Data-Driven GameObject Tweaks](#objectconfigjson--data-driven-gameobject-tweaks)
  - [Fields](#fields)
  - [When Rules Are Applied](#when-rules-are-applied)
  - [Text Alignment Values](#text-alignment-values)
- [Title Screen](#title-screen)
  - [Title Screen Background Color](#title-screen-background-color)
  - [Title Screen Full Background Image](#title-screen-full-background-image)
- [Dialogue System](#dialogue-system)
  - [Speaker Name Prefix](#speaker-name-prefix)
  - [Hide Speaker Tag Bubble](#hide-speaker-tag-bubble)
  - [Speaker Portraits](#speaker-portraits)
  - [Menu Portraits Override](#menu-portraits-override-ff2-ff4-ff6)
  - [Speaker Name Overrides](#speaker-name-overrides)
  - [Dialogue Font Size](#dialogue-font-size)
- [Font Diagnostic & Custom Font Swap](#font-diagnostic--custom-font-swap)
- [UI Tweaks](#ui-tweaks)
  - [Scaled-Down Menu](#scaled-down-menu)
  - [Save Highlight Color](#save-highlight-color)
- [Utility](#utility)
  - [Disable Mouse Cursor](#disable-mouse-cursor)
  - [Force VSync](#force-vsync)
- [Optional Dependencies](#optional-dependencies)

---

## Features

- BepInEx IL2CPP plugin structure (`BasePlugin`) with Harmony runtime patching (`PatchAll`)
- Layered custom texture system with pack-folder selection and hot-reload
- Path-based (`GameAssets/…`) texture overrides to resolve same-name collisions across bundles
- Optional sidecar JSON metadata per texture (size, pivot, border, filter, flip, etc.)
- DDS texture support (DXT1, DXT5, uncompressed RGBA32)
- Data-driven GameObject tweaks via `ObjectConfig.json` (no C# required)
- Custom full-screen title background image injection
- Configurable title screen background color
- Speaker name prepended to dialogue messages
- Speaker tag bubble hider
- Dynamic speaker portrait injection
- Configurable dialogue font size
- Custom font swap via `fontconfig.json` with per-language and per-FontType granularity
- Scaled-down in-game menu (10% shrink)
- Save slot highlight color override
- Mouse cursor hider
- Force VSync
- Soft dependency detection for `Memoria.FFPR` and `Magicite`

---

## Build Requirements

1. Install BepInEx IL2CPP (6.0-pre.2 or newer) into your FFPR game folder.
2. Ensure interop assemblies are generated (`BepInEx/interop`).
3. Build with `BepInExDir` pointing to that game's BepInEx folder.

```powershell
dotnet build .\KupoUI.PR.csproj -c Release -p:BepInExDir="D:\Games\FINAL FANTASY II PR\BepInEx"
```

---

## Install

Copy the output DLL from:

```
bin/Release/net472/KupoUI.PR.dll
```

to:

```
BepInEx/plugins/KupoUI.PR/
```

---

## Configuration Reference

The config file is generated on first run at:

```
BepInEx/config/faospark.kupoui.pr.cfg
```

| Section                 | Key                           | Default    | Description                                                                                                                   |
| ----------------------- | ----------------------------- | ---------- | ----------------------------------------------------------------------------------------------------------------------------- |
| `FontSwap`              | `Enabled`                     | `false`    | Enable custom font swap via `fontconfig.json` under `Modules/Shared/Fonts/`.                                                  |
| `UI`                    | `SaveHighlightColor`          | `Disable`  | Save slot highlight color. Options: `Original`, `DarkNavy`, `DarkGreen`, `DarkViolet`, `DarkYellow`, `DarkOrange`, `Disable`. |
| `UI`                    | `ScaledDownMenu`              | `true`     | Shrinks the in-game menu by 10%.                                                                                              |
| `UI`                    | `TitleScreenBgColor`          | `original` | Title screen background color. Options: `original`, `white`, `black`, `navy`, `crimson`, `violet`.                            |
| `UI-Dialog`             | `DialogueFontSize`            | `36`       | Font size for dialogue text. Use an integer (e.g. `36`) or `Auto` to use the font's declared size.                            |
| `UI-Dialog`             | `MessageSpeakerPrefix`        | `true`     | Prepend speaker name to dialogue messages.                                                                                    |
| `UI-Dialog`             | `SpeakerNameUppercase`        | `false`    | Transform speaker name to UPPERCASE before prepending.                                                                        |
| `UI-Dialog`             | `HideSpeakerTag`              | `true`     | Move the speaker tag bubble off-screen. May conflict with mods that use the bubble as portraits.                              |
| `UI-Dialog`             | `EnableSpeakerPortraits`      | `true`     | Dynamically inject speaker portraits during dialogue.                                                                         |
| `UI-Dialog`             | `FlipSpeakerPortraits`        | `true`     | Flip all injected speaker portraits horizontally.                                                                             |
| `UI-Dialog`             | `SpeakerPortraitsPadding`     | `0,0,0,0`  | Padding for speaker portraits in `left,top,right,bottom` pixels format (e.g. `10,15,0,20`).                                  |
| `UI and Customizations` | `UIThemesFolder`              | _(empty)_  | Folder under `Modules/01-UI-Themes/` for UI theme overrides.                                                                  |                                                                  |
| `UI and Customizations` | `UiFramesFolder`              | _(empty)_  | Folder under `Modules/02-UI-Frames/` for UI frame overrides.                                                                  |
| `UI and Customizations` | `UIBgColorFolder`             | _(empty)_  | Folder under `Modules/03-UI-BgColor/` for UI background overrides.                                                            |
| `UI and Customizations` | `CursorsFolder`               | _(empty)_  | Folder under `Modules/04-UI-Cursors/` for cursor overrides.                                                                   |
| `UI and Customizations` | `ButtonPromptsFolder`         | _(empty)_  | Folder under `Modules/05-Button-Prompts/` for button prompt overrides.                                                        |
| `Utility`               | `DisableMouseCursor`          | `false`    | Hide the OS mouse cursor inside the game window.                                                                              |
| `Utility`               | `ForceVSync`                  | `false`    | Force VSync on and lock `targetFrameRate` to `-1`.                                                                            |
| `Utility`               | `EnableTextureHotReload`      | `false`    | Watch texture folders and rebuild index when files change.                                                                    |
| `Utility`               | `TextureHotReloadDebounceMs`  | `350`      | Debounce window (ms) before rebuilding index after file changes.                                                              |
| `Utility`               | `EnableDDSTextures`           | `true`     | Enable DDS texture loading (DXT1/DXT5 and uncompressed RGBA32).                                                               |
| `Z - Diagnostics`       | `TextureLogger`               | `Off`      | Texture logger mode: `Off`, `Discoveries`, `Resolutions`, `Misses`, `All` (or comma-separated).                               |
| `Z - Diagnostics`       | `LogFontMapping`              | `false`    | Log `FontManager` font parameter and instance details to identify `FontType` mappings.                                        |
| `Z - Diagnostics`       | `MessageSpeakerPrefixLogging` | `false`    | Log speaker name replacements.                                                                                                |
| `Z - Diagnostics`       | `PortraitLogging`             | `true`     | Log portrait lifecycle and resolution details.                                                                                |

---

## Custom Texture System

### Folder Layout

The texture root is fixed to:

```
<GameRoot>/Modules/
```

Recommended structure created automatically on first run:

```
<GameRoot>/
  Modules/
    00-Mods/              ← general shared overrides and custom mods
    01-UI-Themes/         ← full UI theme packs
    02-UI-Frames/         ← UI frame texture packs
    03-UI-BgColor/        ← UI background color packs
    04-UI-Cursors/        ← cursor texture packs
    05-Button-Prompts/    ← button prompt texture packs
    Shared/               ← cross-game textures, speaker portraits, and custom font configurations
      SpeakerPortraits/   ← portrait images resolved by speaker ID
      Fonts/              ← font configuration files (fontconfig.json)
      FF1/                ← FF1-specific textures (game-tag folder)
      FF2/
      FF3/
      FF4/
      FF5/
      FF6/
```

Within each numbered folder you can create named sub-folders (packs). The active pack for each category is selected via the corresponding config key (e.g. `UIThemesFolder = MyTheme` selects `01-UI-Themes/MyTheme/`). An empty value means no pack is selected for that category.

The `Shared/` folder is auto-created on first run. Place textures that apply to all six games directly inside `Shared/`, or inside the matching game-tag sub-folder (e.g. `Shared/FF2/`) to target a specific game. Speaker portraits belong in `Shared/SpeakerPortraits/`.

### Lookup Priority

Priority is highest to lowest:

1. `05-Button-Prompts/<ButtonPromptsFolder>`
2. `04-UI-Cursors/<CursorsFolder>`
3. `03-UI-BgColor/<UIBgColorFolder>`
4. `02-UI-Frames/<UiFramesFolder>`
5. `01-UI-Themes/<UIThemesFolder>`
6. `00-Mods/`
7. `00-Mods/<GameTag>/` (e.g. `00-Mods/FF2/`)
8. `00-Mods/Shared/`
9. `<GameTag>/` (root-level game-tag folder, if present)
10. `Shared/<GameTag>/` (e.g. `Shared/FF2/`)
11. `Shared/` (cross-game, lowest priority)

Use the file name **without extension** to match the in-game texture/sprite name (e.g. `window_frame.png` replaces the asset named `window_frame`).

### Path-Based Overrides

Many FFPR assets share the same file name across different bundles (e.g. `Default_00.png` used in multiple portrait folders). To avoid collisions, KupoUI.PR supports path-based resolution for files placed under a `GameAssets/` folder within any mod folder.

- If a replacement file path contains a `GameAssets/…` segment, it is indexed by its full relative path (no extension), not by name alone.
- At runtime, when the game loads an address like `Assets/GameAssets/…`, KupoUI.PR resolves to the exact matching replacement first.
- Name-only matching still works as a fallback.

**Example — FF2 portrait:**

- In-game address: `Assets/GameAssets/Serial/Res/Chara/Face/FA_FF2_P001/Default_00.png`
- Replacement file: `<GameRoot>/Modules/00-Mods/GameAssets/Serial/Res/Chara/Face/FA_FF2_P001/Default_00.png`

**Prefabs & Container Patterns (.prefab):**

If a texture/sprite is referenced inside a `.prefab` addressable, the mod tracks it and resolves it using three container-aware rules to keep your directories clean (note: runtime addresses omit the `.prefab` extension):

1. **Folder-Matching Containers:** If the texture name matches the containing directory name (e.g. texture `BG_FF4_01` inside `BG_FF4_01/BgPrefab`), the redundant parent directory and prefab segments are simplified.
   - In-game address: `Assets/GameAssets/Serial/Res/Battle/Background/BG_FF4_01/BgPrefab` (texture: `BG_FF4_01`)
   - Replacement file: `<GameRoot>/Modules/00-Mods/GameAssets/Serial/Res/Battle/Background/BG_FF4_01.png`
2. **Generic Prefabs:** If the prefab file is a generic wrapper named `BgPrefab`, the `BgPrefab` segment is omitted.
   - In-game address: `Assets/GameAssets/Serial/Res/Battle/Background/BG_FF4_01/BgPrefab` (texture: `BG_FF4_01_diffuse`)
   - Replacement file: `<GameRoot>/Modules/00-Mods/GameAssets/Serial/Res/Battle/Background/BG_FF4_01/BG_FF4_01_diffuse.png`
3. **Standard Prefabs:** For standard nested prefabs, the prefab name is included in the subfolder namespace to prevent texture name collisions.
   - In-game address: `Assets/GameAssets/Serial/Res/UI/SomePrefab` (texture: `window_frame`)
   - Replacement file: `<GameRoot>/Modules/00-Mods/GameAssets/Serial/Res/UI/SomePrefab/window_frame.png`

### Sidecar Metadata (.json)

Place a `.json` file next to any replacement texture with the same base name to override sprite properties. All fields are optional — only include what you need.

**Example:**

```
Default_00.png
Default_00.json
```

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
  "rectY": 16,
  "flipHorizontal": false
}
```

| Field                       | Description                                                                                          |
| --------------------------- | ---------------------------------------------------------------------------------------------------- |
| `width`                     | Logical source width used to calculate replacement sprite scale.                                     |
| `height`                    | Logical source height used to calculate replacement sprite scale.                                    |
| `pixelsPerUnit`             | Direct sprite PPU override (takes priority over auto scale calculation).                             |
| `filterMode` / `filterType` | Filter override: `Point`, `Bilinear`, or `Trilinear`. String mode takes priority over `pointFilter`. |
| `pointFilter`               | Legacy boolean shorthand: `true` = `Point`, `false` = `Bilinear`.                                    |
| `wrapMode`                  | Wrap mode: `Clamp`, `Repeat`, `Mirror`, `MirrorOnce`. Default: `Clamp`.                              |
| `pivot`                     | Normalized sprite anchor `"x,y"` (0–1). E.g. `"0.5,0.5"` = center, `"0,0"` = bottom-left.            |
| `border`                    | 9-slice border in pixels `"left,bottom,right,top"`. Use `"0,0,0,0"` to strip an inherited border.    |
| `rectX`                     | Pixel X offset within the replacement texture (source UV position, not screen position).             |
| `rectY`                     | Pixel Y offset within the replacement texture. Useful for sprite sheets.                             |
| `flipHorizontal` / `flipX`  | Flip the replacement texture horizontally.                                                           |

> **Note:** When `width`/`height` are provided, sprite creation uses them to override replacement rect sizing; when values do not fit atlas coordinates, origin-clamped sizing is used as a fallback.

### Texture Formats & Filter Modes

**Supported formats:** `png`, `jpg`, `jpeg`, `tga`, `dds` (DXT1, DXT5, uncompressed RGBA32)

**Filter mode behavior:**

- Default is Bilinear.
- Point filtering is applied automatically if the replacement file is inside a folder named `Pixel` or `Pixels` (at any depth).
- For path-based `GameAssets/…` overrides, prefer sidecar `filterMode` metadata when the folder convention is not practical.

### Hot-Reload

When `Utility.EnableTextureHotReload` is `true`, file-system changes inside the `Modules/` folder trigger an automatic texture index rebuild. Rebuilds are debounced by `TextureHotReloadDebounceMs` (default 350 ms) to avoid repeated rebuilds while copying many files.

### Texture Logger

Controlled by `Z - Diagnostics.TextureLogger`. Categories:

- `Discoveries` — unique texture names seen from sprite/texture hooks.
- `Resolutions` — unique names that successfully map to a replacement file.
- `Misses` — names that were looked up but found no replacement. Optional; can be noisy.

Set to `All` to enable all categories, or use a comma-separated list (e.g. `Discoveries,Resolutions`).

---

## ObjectConfig.json — Data-Driven GameObject Tweaks

Manipulate Unity GameObjects at runtime (position, rotation, scale, active state, text properties) without writing C# — just drop an `ObjectConfig.json` file anywhere inside `Modules/`.

The plugin scans **all** `ObjectConfig.json` files found recursively under `Modules/` on startup. Files placed inside `Shared/FF1`–`FF6` sub-folders are filtered to the detected game, so only the matching game's rules are applied.

### Folder Placement

```
<GameRoot>/
  Modules/
    00-Mods/
      MyMod/
        ObjectConfig.json   ← picked up
    01-UI-Themes/
      MyTheme/
        ObjectConfig.json   ← also picked up
    Shared/
      ObjectConfig.json     ← picked up (applies to all games)
      FF2/
        ObjectConfig.json   ← picked up only when running FF2
```

### File Format

```json
{
  "objects": [
    {
      "TargetObjectName": "menu_base(Clone)",
      "TargetPath": "Canvas/aspect_parent/menu_parent/menu_base(Clone)",
      "SceneName": "Title",
      "Position": { "x": 0, "y": -50, "z": 0 },
      "Rotation": { "x": 0, "y": 0, "z": 0 },
      "Scale": { "x": 0.9, "y": 0.9, "z": 1.0 },
      "Size": { "x": 300, "y": 100 },
      "SetActive": true,
      "TextAlignment": "MiddleCenter",
      "FontSize": 24,
      "ResizeTextForBestFit": true,
      "ResizeTextMaxSize": 36,
      "ResizeTextMinSize": 12,
      "TextColorWhite": true,
      "Color": "#FF5500",
      "DisableShadow": true
    }
  ]
}
```

The `objects` array can contain as many entries as you need, spread across one file or multiple files in different mod folders.

### Fields

| Field                  | Required | Description                                                                                                                                                                                                                                                                                                  |
| ---------------------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `TargetObjectName`     | **Yes**  | Exact `GameObject` name to match (e.g. `"menu_base(Clone)"`).                                                                                                                                                                                                                                                |
| `TargetPath`           | No       | Hierarchy path suffix to disambiguate objects with the same name. Forward-slash notation, matched from the object upward. E.g. `"Canvas/aspect_parent/menu_base(Clone)"`.                                                                                                                                    |
| `SceneName`            | No       | Only apply this rule while this scene is active. Omit to apply in every scene. Case-insensitive.                                                                                                                                                                                                             |
| `Position`             | No       | Sets `transform.localPosition`. Provide `x`, `y`, `z` as floats.                                                                                                                                                                                                                                             |
| `Rotation`             | No       | Sets `transform.localEulerAngles` (Euler angles in degrees). Provide `x`, `y`, `z`.                                                                                                                                                                                                                          |
| `Scale`                | No       | Sets `transform.localScale`. Provide `x`, `y`, `z`.                                                                                                                                                                                                                                                          |
| `Size`                 | No       | Sets absolute width and height on `UnityEngine.RectTransform` component (if present) via `SetSizeWithCurrentAnchors`. Provide `x` (width) and `y` (height) floats.                                                                                                                                           |
| `SetActive`            | No       | Calls `gameObject.SetActive(value)`. Use `true` or `false`.                                                                                                                                                                                                                                                  |
| `TextAlignment`        | No       | Sets `Text.alignment` on the `UnityEngine.UI.Text` component (if present). See [Text Alignment Values](#text-alignment-values).                                                                                                                                                                              |
| `ChildAlignment`       | No       | Sets `LayoutGroup.childAlignment` on the `UnityEngine.UI.LayoutGroup` component (if present, e.g. horizontal/vertical/grid layouts). See [Text Alignment Values](#text-alignment-values).                                                                                                                    |
| `FontSize`             | No       | Sets `Text.fontSize` on the `UnityEngine.UI.Text` component (if present). Provide an integer.                                                                                                                                                                                                                |
| `ResizeTextForBestFit` | No       | Sets `Text.resizeTextForBestFit`. Use `true` or `false`.                                                                                                                                                                                                                                                     |
| `ResizeTextMaxSize`    | No       | Sets `Text.resizeTextMaxSize`. Provide an integer.                                                                                                                                                                                                                                                           |
| `ResizeTextMinSize`    | No       | Sets `Text.resizeTextMinSize`. Provide an integer.                                                                                                                                                                                                                                                           |
| `TextColorWhite`       | No       | Legacy shortcut to force `Text.color` to white. Use `Color` for custom colors.                                                                                                                                                                                                                               |
| `Color`                | No       | Forces `Graphic.color` on `UnityEngine.UI.Graphic` components (`Text`, `Image`, `RawImage`). Re-enforced on every color write to prevent game overrides. Accepts Hex string (e.g. `"#FF5500"`, `"#FF5500FF"`), color name (`"red"`, `"white"`), or RGBA object (`{"r": 1.0, "g": 0.5, "b": 0.0, "a": 1.0}`). |
| `DisableShadow`        | No       | Disables all `UnityEngine.UI.Shadow` components on the matching GameObject. Use `true`.                                                                                                                                                                                                                      |

> **Note:** All fields except `TargetObjectName` are optional. Only include the ones you want to change — unspecified fields leave the object unchanged.

### When Rules Are Applied

Rules fire at two moments:

1. **Scene load** — when a scene finishes loading, all GameObjects in the scene are scanned and matching rules are applied.
2. **SetActive(true)** — when any GameObject is enabled at runtime, matching rules are applied immediately.

### Using `TargetPath` to Avoid Wrong Matches

If multiple objects share the same name (common in FFPR), add `TargetPath` to target only the one you want:

```json
{
  "TargetObjectName": "menu_base(Clone)",
  "TargetPath": "RootObject/Canvas/aspect_parent/menu_parent/menu_base(Clone)",
  "Scale": { "x": 0.9, "y": 0.9, "z": 1.0 }
}
```

The path is matched by walking up the transform hierarchy, so it does not need to start from the scene root — a suffix is enough.

> **Important:** Two common mistakes to avoid:
>
> - The **last segment of `TargetPath` must match `TargetObjectName`** exactly. The matcher walks upward from the object itself.
> - **No trailing slash.** A path ending with `/` produces an empty final segment that will never match, causing the rule to silently do nothing.

### Hiding an Object

```json
{
  "TargetObjectName": "some_ui_element",
  "SceneName": "MainMenu",
  "SetActive": false
}
```

> **Note on `SetActive: false` behaviour:** The rule uses a Harmony prefix that intercepts every `SetActive(true)` call and flips it to `false` before Unity processes it. This permanently prevents the object from becoming active — no flicker, no one-frame delay.

### Text Alignment Values

| Value          | Description                        |
| -------------- | ---------------------------------- |
| `UpperLeft`    | Top-left corner                    |
| `UpperCenter`  | Top-center                         |
| `UpperRight`   | Top-right corner                   |
| `MiddleLeft`   | Vertically centered, left-aligned  |
| `MiddleCenter` | Fully centered                     |
| `MiddleRight`  | Vertically centered, right-aligned |
| `LowerLeft`    | Bottom-left corner                 |
| `LowerCenter`  | Bottom-center                      |
| `LowerRight`   | Bottom-right corner                |

Values are case-insensitive. If the object has no corresponding component (`Text` for `TextAlignment`, or `LayoutGroup` for `ChildAlignment`), or the value is unrecognized, a warning is written to the log and the field is skipped.

---

## Title Screen

### Title Screen Background Color

`UI.TitleScreenBgColor` — Controls the color of the title screen's solid background panel.

Options: `original` (game default), `white`, `black`, `navy`, `crimson`, `violet`.

The patch intercepts the `Graphic.color` setter and re-enforces the chosen color on every material update to prevent the game from overriding it.

### Title Screen Full Background Image

Drop any supported image named `TitlescreenFullBG` into any mod folder to inject a custom full-screen background image on the title screen. No config entry is required — if the file is absent, nothing happens.

```
<GameRoot>/Modules/00-Mods/MyMod/TitlescreenFullBG.png
```

**Supported formats:** `png`, `jpg`, `jpeg`, `tga`, `dds`

**How it works:**

The patch watches for the title screen's internal `background` object at:

```
background_canvas/ui_root/backgrou_root/background
```

When that object activates, a new `fullbg` `RawImage` GameObject is injected as a sibling immediately above it, stretched to fill the parent rect:

```
background_canvas/ui_root/backgrou_root/
  ├── background   ← original solid-color background (still tinted by TitleScreenBgColor)
  └── fullbg       ← injected — renders on top, covers background
```

**Notes:**

- The object is only created once per activation cycle — no duplicates on re-activation.
- The texture is kept alive with `DontDestroyOnLoad` to survive additive scene reloads.
- For best results, use an image sized to your target resolution (e.g. 1920×1080).

---

## Dialogue System

### Speaker Name Prefix

`UI-Dialog.MessageSpeakerPrefix` (default `true`) — Prepends the speaker's name to the dialogue message text inside the message window, without modifying any game files.

- `UI-Dialog.SpeakerNameUppercase` (default `false`) — Transform the speaker name to UPPERCASE before prepending.
- When the active language is Japanese, the separator changes from `": "` to `「` automatically.
- Guards against double-prefix if the setter fires twice on the same text.
- Works as an alternative to Classic Text Box Framework for displaying speaker names.

### Hide Speaker Tag Bubble

`UI-Dialog.HideSpeakerTag` (default `true`) — Hides the speaker name tag:

- For normal message windows, moves the `speker_root` bubble off-screen so the speaker tag is invisible but the underlying object remains active.
- For battle message windows, deactivates the left and right individual `speaker` tag GameObjects entirely.

> **Note:** This will conflict with older mods that use the speaker tag bubble as a portrait display.

### Speaker Portraits

`UI-Dialog.EnableSpeakerPortraits` (default `true`) — Dynamically injects a speaker portrait image inside the message window during dialogue sequences.

- Portraits are resolved from the texture index using the speaker ID. Place portrait images in any mod folder matching the speaker asset name.
- `UI-Dialog.FlipSpeakerPortraits` (default `true`) — Flip all injected portraits horizontally.
- `UI-Dialog.SpeakerPortraitsPadding` (default `0,0,0,0`) — Offset padding `left,top,right,bottom` in pixels to shrink and shift the injected portrait container.
- Portrait images are cached in memory after first load.
- Uses the same folder priority as the main texture system.
- `Z - Diagnostics.PortraitLogging` (default `true`) — Logs portrait lifecycle and resolution details.
- **Note:** Injected speaker portraits are automatically disabled inside battle message windows.

### Menu Portraits Override (FF2, FF4, FF6)

In Final Fantasy 2, 4, and 6, character portraits are displayed in the main game menu. You can override these menu portraits by explicitly mapping them to your custom speaker portraits using `MenuPortraitMap.json` files.

#### File Location

`MenuPortraitMap.json` can be placed in **any sub-folder under `Modules/`**. The plugin scans all of them recursively at startup and merges them.

```
<GameRoot>/Modules/
  Shared/
    SpeakerPortraits/
      MenuPortraitMap.json         ← recommended location
      MenuPortraitMap-sample.json  ← auto-generated reference (overwritten each launch)
```

#### Mapping Format

Inside `MenuPortraitMap.json`, define key-value pairs where the key is the menu portrait address or Speaker ID, and the value is the target Speaker ID/name or portrait image filename:

```json
{
  "Assets/GameAssets/Serial/Res/Chara/Face/FA_FF4_P001/Default_00": "Cecil",
  "FA_FF4_P002": "SPEAKER_05",
  "P003": "Rydia"
}
```

If mapped to a dialogue Speaker ID (like `SPEAKER_05`), the plugin will automatically resolve its display name from `speaker-names.json` (e.g., `"SPEAKER_05": "Kain"`) and search the BepInEx `SpeakerPortraits/` folders for either `SPEAKER_05.png` or `Kain.png`.

#### Zero-Config Fallback (No JSON mapping needed)

If no mapping is defined in `MenuPortraitMap.json`, the plugin automatically falls back to searching for a custom portrait matching:

1. The full speaker ID (e.g. `FA_FF4_P001.png`)
2. The shorthand ID (e.g. `P001.png`)
3. The display name override in `speaker-names.json` (if any exists for that ID)

### Speaker Name Overrides

`speaker-names.json` lets you register speaker IDs with display names and override speaker identity on a per-dialogue-key basis — all without touching game files.

#### File Location

`speaker-names.json` can be placed in **any sub-folder under `Modules/`** — the plugin scans all of them recursively and merges every file it finds.

```
<GameRoot>/Modules/
  Shared/
    SpeakerPortraits/
      speaker-names.json         ← recommended location
      speaker-names-sample.json  ← auto-generated reference (overwritten each launch)
    FF2/
      speaker-names.json         ← game-specific (only used when running FF2)
  00-Mods/
    MyMod/
      speaker-names.json         ← mod-specific
  01-UI-Themes/
    MyTheme/
      speaker-names.json         ← inside a theme pack
```

Files are loaded in **alphabetical path order**. When multiple files define the same key, the **last file wins** — so a file deeper in the folder hierarchy or later alphabetically takes priority.

#### File Format

```json
{
  "speakers": {
    "SPEAKER_77": "Crewman",
    "SPEAKER_80": "Old Man"
  },
  "messageOverrides": {
    "E0001_00_001_a_01": {
      "speakerId": "SPEAKER_77",
      "speakerName": "Crewman"
    },
    "E0001_00_002_a_01": { "speakerName": "Old Man" }
  }
}
```

#### `speakers` — Register speaker IDs

Maps a speaker ID to a display name. **Always applied** when that speaker is active — overrides whatever name the game provides (not just a fallback for blank names).

| Key                                     | Value                         |
| --------------------------------------- | ----------------------------- |
| Internal speaker ID (e.g. `SPEAKER_77`) | Display name (e.g. `Crewman`) |

- Case-insensitive keys.
- Keys beginning with `_` are treated as comments and skipped.
- Applies to both the dialogue prefix text and portrait image lookup.

#### `messageOverrides` — Override by dialogue key

Overrides the speaker ID and/or name for a **specific dialogue message key**. Takes the highest priority — beats both the game's data and the `speakers` block.

Each entry maps a dialogue key to an object with optional fields:

| Field         | Description                                                                         |
| ------------- | ----------------------------------------------------------------------------------- |
| `speakerId`   | Force a specific speaker ID for portrait lookup. Optional.                          |
| `speakerName` | Force a specific display name for the prefix and portrait-by-name lookup. Optional. |

Both fields are optional. You can provide just `speakerName` to relabel a line without changing portrait lookup, or just `speakerId` to redirect portrait resolution.

#### Priority order

When a dialogue line is displayed, the effective speaker name and ID are resolved in this order:

| Priority | Source                          | Condition                                        |
| -------- | ------------------------------- | ------------------------------------------------ |
| 1        | `messageOverrides[dialogueKey]` | Most specific — wins everything                  |
| 2        | `speakers[speakerId]`           | Always applied when the speaker ID is registered |
| 3        | Game's own speaker text         | Used as-is if nothing above matches              |

#### How to find a speaker ID or dialogue key

Enable `Z - Diagnostics.MessageSpeakerPrefixLogging = true` in the BepInEx config, then trigger the dialogue line. Look for a log entry like:

```
[MessageSpeakerPrefix] Dialogue matched. Key: 'E0001_00_001_a_01', SpeakerID: 'SPEAKER_77', SpeakerName: '(null)', Message: '...'
```

- `Key` → use as a `messageOverrides` key
- `SpeakerID` → use as a `speakers` key

#### Portrait images

Portrait files are resolved using the **effective** speaker ID and name after overrides are applied. Drop either into the `SpeakerPortraits` folder:

- `SPEAKER_77.png` — matched by speaker ID
- `Crewman.png` — matched by display name

### Dialogue Font Size

`UI-Dialog.DialogueFontSize` (default `36`) — Enforces a fixed font size on both the message text and speaker text components inside `MessageWindowView`.

- Set to an integer (e.g. `36`, `40`, `48`) to apply a specific size.
- Set to `Auto` to use the font's declared size in-game (effectively disables enforcement).
- The patch also forces `resizeTextForBestFit` to `false` on dialogue text to prevent the game from overriding the size.
- Works independently of `MessageSpeakerPrefix` — neither needs to be enabled for the other to function.

---

## Font Diagnostic & Custom Font Swap

This plugin includes a two-phase font mapping and replacement utility.

### Phase 1 — Diagnostic Logging

When the game initializes fonts, `[FontMap]` log entries are written to `BepInEx/LogOutput.log`:

```
[Info   :KupoUI.PR] [FontMap] FontType=Font09 | Language=En | FontName=PIXELREMASTERFONT.ttf | LineSpace=0.66 | Font=
```

This identifies which `FontType` enum value corresponds to which language and default asset.

- `Z - Diagnostics.LogFontMapping` (default `false`) — Enable this diagnostic.

### Phase 2 — Custom Font Swap

`FontSwap.Enabled` (default `false`) — Enable font swapping. Once enabled, fonts are configured via `fontconfig.json`.

#### File Locations

```
<GameRoot>/Modules/Shared/Fonts/
  fontconfig.json         ← your active font configuration
  font-help.txt           ← auto-generated help guide (contains baseline defaults at the bottom)
```

Both files are created automatically on first startup.

#### Configuration File Format

The mapping file supports both **simple string values** and **object-based values**.

```json
{
  "En": {
    "Font01": { "FontName": "Segoe UI", "LineSpace": 1.0 },
    "Font02": { "FontName": "Arial", "LineSpace": 1.2 },
    "Default": { "FontName": "Arial", "LineSpace": 1.2 }
  },
  "Ja": {
    "Font01": { "FontName": "FOT-NewRodinPro-DB", "LineSpace": 0.73 }
  }
}
```

| Field       | Description                                                                                       |
| ----------- | ------------------------------------------------------------------------------------------------- |
| `FontName`  | Font family name (e.g. `"Segoe UI"`, `"Consolas"`). Required.                                     |
| `LineSpace` | Line height factor (e.g. `1.2`). Adjust if your font appears cramped or overflows dialogue boxes. |

#### Language-Specific Configuration Styles

##### Style A — Root-Level Language Specifier (single-language mods)

```json
{
  "Language": "Pt",
  "Font01": { "FontName": "PortugueseFont" },
  "Default": "PortugueseFallbackFont"
}
```

##### Style B — Nested Language Blocks (multi-language mods)

```json
{
  "Pt": { "Font01": { "FontName": "PortugueseFont" } },
  "Ja": { "Font01": { "FontName": "JapaneseFont" } }
}
```

##### Style C — Flat Key Suffixes

```json
{
  "Font01": { "FontName": "EnglishFont" },
  "Font01_Ja": { "FontName": "JapaneseFont", "LineSpace": 0.83 },
  "Default_Ja": "JapaneseFallbackFont"
}
```

#### Fallback Lookup Order

When looking up a font for a specific `FontType` and language:

1. Specific FontType + Specific Language (e.g. `Font01_Ja` or nested `Ja` → `Font01`)
2. Specific FontType global fallback (e.g. `Font01`)
3. `Default` + Specific Language (e.g. `Default_Ja` or nested `Ja` → `Default`)
4. `Default` global fallback

#### Supported Languages

`En`, `Ja`, `Fr`, `De`, `It`, `Ru`, `Pt`, `Th`, `Ko`, `Zht`, `Zhc`

#### Enabling the Swap

1. Open `BepInEx/config/faospark.kupoui.pr.cfg`.
2. Set `FontSwap.Enabled` to `true`.
3. Restart the game.

> Custom fonts are cached in memory upon first load — zero performance impact during scene transitions.

---

## UI Tweaks

### Scaled-Down Menu

`UI.ScaledDownMenu` (default `true`) — Shrinks the in-game menu by 10% by setting `localScale` to `(0.9, 0.9, 1.0)` on:

- `Canvas/aspect_parent/menu_parent/menu_base(Clone)`
- `RootObject/sab_canvas/root/ui_root`

### Save Highlight Color

`UI.SaveHighlightColor` (default `Disable`) — Overrides the Quick Save and Auto Save slot highlight color.

Options: `Original` (game default), `DarkNavy`, `DarkGreen`, `DarkViolet`, `DarkYellow`, `DarkOrange`, `Disable` (removes the highlight entirely).

### Menu Portrait Aspect Ratio Preservation

Automatically preserves the aspect ratio of custom character portraits displayed on the main menu screen (by setting `preserveAspect = true` and bypassing the default `SetNativeSize` execution on the `/chara_rect/front/front_parent/charac_parent/chara_image` UI Image component). This ensures that custom high-resolution character portraits do not stretch or distort.

---

## Utility

### Disable Mouse Cursor

`Utility.DisableMouseCursor` (default `false`) — Hides the OS mouse cursor inside the game window using the Unity `Cursor` API.

### Force VSync

`Utility.ForceVSync` (default `false`) — Forces `QualitySettings.vSyncCount = 1` and `Application.targetFrameRate = -1` on startup, and intercepts any game writes that would override these values.

---

## Optional Dependencies

KupoUI.PR does not hard-reference `Memoria.FFPR` or `Magicite`. At runtime it checks loaded assemblies and logs whether those mods are present, enabling future integration paths without breaking standalone execution.
