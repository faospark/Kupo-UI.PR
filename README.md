# KupoUI.PR

A BepInEx IL2CPP plugin for the Final Fantasy Pixel Remaster series (FF1–FF6) that provides runtime UI patches, custom texture replacement, dialogue enhancements, and data-driven GameObject tweaks.

### Background & Origins

KupoUI.PR grew out of the development of the new version of _Darker UI_. It was originally designed to resolve major stress points in FFPR UI development. Because of the way the game handles assets, making UI mods for the Pixel Remasters has historically been tedious and required editing dozens of redundant files.

After spending time modding another game and learning new approaches, I applied those lessons here. What began as a simple tool to address specific UI pain points quickly expanded into a much larger framework. Eventually, I decided to separate it into a standalone BepInEx plugin. Its name, KupoUI.PR, is a nod to the classic Kupo UI mod for Final Fantasy IX.

This framework is not intended to replace Magicite, Memoria, or FFPRFix. While there is some functional overlap, each tool serves a different purpose and should be used in tandem.

### Incompatibilities

- **Memoria's Classic Text Box Framework**: Not recommended. Both frameworks modify the text box UI, but they use fundamentally different methods that will conflict.
- **Speaker Portrait Mods**: Any mod that alters the structure of the dialogue message box to insert portraits may conflict with the built-in speaker portrait injector.

---

## Table of Contents

- [KupoUI.PR](#kupouipr)
  - [Table of Contents](#table-of-contents)
  - [Core Engine Architecture](#core-engine-architecture)
  - [Build Requirements](#build-requirements)
  - [Install](#install)
    - [Linux \& Steam Deck Compatibility](#linux--steam-deck-compatibility)
  - [Configuration Reference](#configuration-reference)
  - [1. 🎨 Texture \& Theme Engine](#1--texture--theme-engine)
    - [Key Benefits](#key-benefits)
    - [Folder Layout](#folder-layout)
    - [Lookup Priority](#lookup-priority)
    - [Ignoring / Game-Tag / Blocking Folders](#ignoring--game-tag--blocking-folders)
    - [Path-Based Overrides](#path-based-overrides)
    - [Prefabs \& Battle Background Support](#prefabs--battle-background-support)
    - [Sidecar Metadata (.json)](#sidecar-metadata-json)
    - [Texture Formats \& Filter Modes](#texture-formats--filter-modes)
    - [Hot-Reload](#hot-reload)
  - [2. 📐 GameObject \& Layout Engine (ObjectConfig.json)](#2--gameobject--layout-engine-objectconfigjson)
    - [Folder Placement](#folder-placement)
    - [File Format](#file-format)
    - [Fields](#fields)
    - [Supported Color Names](#supported-color-names)
    - [When Rules Are Applied](#when-rules-are-applied)
    - [Using `TargetPath` to Avoid Wrong Matches](#using-targetpath-to-avoid-wrong-matches)
    - [Disabling a Mask on a Specific Object](#disabling-a-mask-on-a-specific-object)
    - [Hiding an Object](#hiding-an-object)
    - [Inserting Custom Image Objects (`NewImages`)](#inserting-custom-image-objects-newimages)
    - [Text Alignment Values](#text-alignment-values)
    - [Scaled-Down Menu](#scaled-down-menu)
    - [Save Highlight Color](#save-highlight-color)
    - [Title Screen \& Main Menu Background Images](#title-screen--main-menu-background-images)
  - [3. 💬 Dialogue \& Portrait Engine](#3--dialogue--portrait-engine)
    - [Speaker Name Prefix](#speaker-name-prefix)
    - [Hide Speaker Tag Bubble](#hide-speaker-tag-bubble)
    - [Speaker Portraits](#speaker-portraits)
    - [Menu Portraits Override (FF2, FF4, FF6)](#menu-portraits-override-ff2-ff4-ff6)
      - [Menu Portrait Aspect Ratio Preservation](#menu-portrait-aspect-ratio-preservation)
    - [Speaker Name Overrides](#speaker-name-overrides)
    - [Dialogue Font Size](#dialogue-font-size)
  - [4. 🔤 Font \& Typography Engine (fontconfig.json)](#4--font--typography-engine-fontconfigjson)
    - [Phase 1 — Diagnostic Logging](#phase-1--diagnostic-logging)
    - [Phase 2 — Custom Font Swap](#phase-2--custom-font-swap)
      - [Supported System Fonts](#supported-system-fonts)
      - [Note for Linux \& Steam Deck Users (via Proton)](#note-for-linux--steam-deck-users-via-proton)
  - [5. 📝 Text \& Inline Icon Engine](#5--text--inline-icon-engine)
    - [TextConfig.json — Data-Driven Text Customization](#textconfigjson--data-driven-text-customization)
    - [IconsConfig.json — Custom Rich Text Inline Icons](#iconsconfigjson--custom-rich-text-inline-icons)
    - [Language-Agnostic Icon Injection](#language-agnostic-icon-injection-eg-for-inventory-items)
    - [Performance \& Texture Atlases](#performance--texture-atlases)
    - [Disable Item Dimming](#disable-item-dimming)
  - [6. 🛠️ Logging \& Utility Engine](#6--logging--utility-engine)
    - [Startup Mod Loader \& Texture Conflict Detection](#startup-mod-loader--texture-conflict-detection)
    - [Utility Settings](#utility-settings)
    - [Developer Diagnostic Logging Modes](#developer-diagnostic-logging-modes)
    - [Optional Dependencies](#optional-dependencies)

---

## Core Engine Architecture

KupoUI.PR is structured as a suite of **6 Core Engines**:

### 1. 🎨 Texture & Theme Engine
- **5-Layer Priority Pipeline**: Swap UI themes, frames, bg colors, cursors, & button prompts cleanly.
- **Prefabs & Addressables**: Replace battle backgrounds & prefab assets without bundle editing.
- **Sidecar Metadata**: Fine-tune 9-slice borders, UV wrap modes (auto-tiling), scale, & PPU per image.
- **Hot-Reload**: Live texture updates on disk save without restarting the game.

### 2. 📐 GameObject & Layout Engine (`ObjectConfig.json`)
- **Runtime UI Manipulation**: Move, rotate, scale, hide, or recolor any UI element in the game.
- **Targeted Hierarchy Pathing**: Disambiguate duplicate UI elements via paths & sibling indexes.
- **Custom Image Insertion**: Drop new UI images (`NewImages`) into existing screens without writing C#.
- **Layout Tweaks & Backgrounds**: Scaled-down in-game menu (10% shrink), custom save slot highlight colors, & full-screen title/main menu background images (`TitlescreenFullBG`, `MainMenuBg`).

### 3. 💬 Dialogue & Portrait Engine
- **Dynamic Portrait Injection**: Inject speaker portraits into speech boxes with custom padding & offsets.
- **Speaker Formatting**: Auto-prepend speaker names, UPPERCASE mode, line breaks, & word wrapping.
- **Menu Portrait Remapper**: Custom portraits for FF2, FF4, & FF6 with aspect ratio preservation.

### 4. 🔤 Font & Typography Engine (`fontconfig.json`)
- **Custom Font Swap**: Use system-wide fonts (installed for all users) per language & UI role (`Main`, `Title`, `Battle`, `Number`).
- **Typography Tuning**: Custom font scaling, line spacing, vertical offsets, & dialogue font size lock.

### 5. 📝 Text & Inline Icon Engine (`TextConfig.json` & `IconsConfig.json`)
- **Data-Driven Text Overrides**: Swap text by key, exact string match, or Regex search-and-replace.
- **Rich Text Inline Icons**: Inject custom icon tags (`<IC_BAG>`) into text & items across all languages.
- **Atlas Disk Caching & Full Color**: Auto-packs icons into atlases, caches on disk, & option to disable item dimming.

### 6. 🛠️ Logging & Utility Engine
- **Utility Settings**: Disable mouse cursor, force VSync, DDS textures, & texture hot-reload.
- **Mod Discovery & Conflict Detection**: Scans active mods in `00-Mods/` & alerts on texture conflicts.
- **Developer Diagnostics**: Dedicated loggers to trace textures, battle sprites, fonts, dialogue keys, inline icons, & UI logic.

---

## Build Requirements

1. Install BepInEx IL2CPP (6.0-pre.2 or newer) into your FFPR game folder.
2. Ensure interop assemblies are generated (`BepInEx/interop`).
3. Build with `BepInExDir` pointing to that game's BepInEx folder.

```powershell
dotnet build .\KupoUI.PR.csproj -c Release
```

| IMPORTANT ! : This is just the repo for the DLL of KupoUI.PR . if you want to Experience Darker UI . You need to download the appropriate version from Nexus mods

---

## Install

Copy the output DLL from:

```
bin/Release/net472/KupoUI.PR.dll
```

to:

```
BepInEx/plugins/
```

### Linux & Steam Deck Compatibility

Since BepInEx uses a custom `winhttp.dll` to inject itself into the game, Proton will ignore it by default on Linux and Steam Deck. You must force Proton to load the local version:

1. Right-click the game in your Steam Library and select **Properties...**.
2. In the **General** tab, scroll down to the **Launch Options** section.
3. Paste the following line:
   ```bash
   export WINEDLLOVERRIDES="winhttp=n,b"; %command%
   ```

---

## Configuration Reference

The config file is generated on first run at:

```
BepInEx/config/faospark.kupoui.pr.cfg
```

| Section                 | Key                           | Default    | Description                                                                                                                            |
| ----------------------- | ----------------------------- | ---------- | -------------------------------------------------------------------------------------------------------------------------------------- |
| `FontSwap`              | `Enabled`                     | `false`    | Enable custom font swap via `fontconfig.json` under `Modules/System/` (supports `FontName`, `LineSpace`, `YOffset`, `FontSize`, `FontSizeMin`, and `FontSizeMax`). |
| `UI`                    | `DisableItemDimming`          | `false`    | Forces all item list icons and names to display at full color, ignoring the grey dim tint applied to unusable items.                   |
| `UI`                    | `SaveHighlightColor`          | `Disable`  | Save slot highlight color. Options:`Original`, `DarkNavy`, `DarkGreen`, `DarkViolet`, `DarkYellow`, `DarkOrange`, `Disable`.           |
| `UI`                    | `ScaledDownMenu`              | `true`     | Shrinks the in-game menu by 10%.                                                                                                       |
| `UI`                    | `TitleScreenBgColor`          | `original` | Title screen background color. Options:`original`, `white`, `black`, `navy`, `crimson`, `violet`.                                      |
| `UI-Dialog`             | `DialogueFontSize`            | `Auto`     | Font size for dialogue text. Use an integer (e.g.`36`) or `Auto` to use the font's declared size.                                      |
| `UI-Dialog`             | `MessageSpeakerPrefix`        | `true`     | Prepend speaker name to dialogue messages.                                                                                             |
| `UI-Dialog`             | `SpeakerNameUppercase`        | `false`    | Transform speaker name to UPPERCASE before prepending.                                                                                 |
| `UI-Dialog`             | `SpeakerNameNewLine`          | `false`    | If true, inserts a line break (new line) after the speaker prefix in dialogue boxes.                                                   |
| `UI-Dialog`             | `DialogueTextWrap`            | `true`     | If true, forces built-in text wrapping on dialogue text boxes to prevent horizontal overflow.                                          |
| `UI-Dialog`             | `DialogueLineLengthLimit`     | `0`        | If greater than 0, forces dialogue text to wrap at this maximum character count per line.                                              |
| `UI-Dialog`             | `HideSpeakerTag`              | `true`     | Move the speaker tag bubble off-screen. May conflict with mods that use the bubble as portraits.                                       |
| `UI-Dialog`             | `EnableSpeakerPortraits`      | `true`     | Dynamically inject speaker portraits during dialogue.                                                                                  |
| `UI-Dialog`             | `FlipSpeakerPortraits`        | `true`     | Flip all injected speaker portraits horizontally.                                                                                      |
| `UI-Dialog`             | `SpeakerPortraitsPadding`     | `0,0,0,0`  | Padding for speaker portraits in`left,top,right,bottom` pixels format (e.g. `10,15,0,20`).                                             |
| `UI-Dialog`             | `SpeakerPortraitsTextOffset`  | `0`        | Offset (in pixels) for the dialogue text box when speaker portraits are active. Supports`X` or `X,Y` format (e.g., `-75` or `-75,10`). |
| `UI and Customizations` | `UIThemesFolder`              | _(empty)_  | Folder under`Modules/01-UI-Themes/` for UI theme overrides.                                                                            |
| `UI and Customizations` | `UiFramesFolder`              | _(empty)_  | Folder under`Modules/02-UI-Frames/` for UI frame overrides.                                                                            |
| `UI and Customizations` | `UIBgColorFolder`             | _(empty)_  | Folder under`Modules/03-UI-BgColor/` for UI background overrides.                                                                      |
| `UI and Customizations` | `CursorsFolder`               | _(empty)_  | Folder under`Modules/04-UI-Cursors/` for cursor overrides.                                                                             |
| `UI and Customizations` | `ButtonPromptsFolder`         | _(empty)_  | Folder under`Modules/05-Button-Prompts/` for button prompt overrides.                                                                  |
| `Utility`               | `DisableMouseCursor`          | `false`    | Hide the OS mouse cursor inside the game window.                                                                                       |
| `Utility`               | `ForceVSync`                  | `false`    | Force VSync on and lock`targetFrameRate` to `-1`.                                                                                      |
| `Utility`               | `EnableTextureHotReload`      | `false`    | Watch texture folders and rebuild index when files change.                                                                             |
| `Utility`               | `TextureHotReloadDebounceMs`  | `350`      | Debounce window (ms) before rebuilding index after file changes.                                                                       |
| `Utility`               | `EnableDDSTextures`           | `true`     | Enable DDS texture loading (DXT1/DXT5 and uncompressed RGBA32).                                                                        |
| `Z - Diagnostics`       | `TextureLogger`               | `Off`      | Texture logger mode:`Off`, `Discoveries`, `Resolutions`, `Misses`, `All` (or comma-separated).                                         |
| `Z - Diagnostics`       | `LogFontMapping`              | `false`    | Log`FontManager` font parameter and instance details to identify `FontType` mappings.                                                  |
| `Z - Diagnostics`       | `MessageSpeakerPrefixLogging` | `false`    | Log speaker name replacements.                                                                                                         |
| `Z - Diagnostics`       | `LogAllTexts`                 | `false`    | If true, logs all texts assigned to`UnityEngine.UI.Text` components to the console.                                                    |
| `Z - Diagnostics`       | `IconLogging`                 | `false`    | If true, logs custom icon tag matches and sprite swaps to the console.                                                                 |
| `Z - Diagnostics`       | `PortraitLogging`             | `true`     | Log portrait lifecycle and resolution details.                                                                                         |

---

## Custom Texture System

The custom texture system makes installing and developing UI and button prompt mods incredibly straightforward.

### Key Benefits

- **Zero Bundle Editing**: You no longer need to unpack, edit, and repack the game's Unity `.bundle` files for UI elements.
- **Drop-in Folders**: UI themes, custom frames, backgrounds, cursors, and button prompts can simply be placed inside a named folder under their respective category (e.g., `01-UI-Themes/Darker UI/` or `05-Button-Prompts/0 PS5/`).
- **Asset Name Matching**: Simply name your custom asset files (`.png`, `.dds`, etc.) to match the internal name of the in-game texture or sprite you want to replace (e.g., naming your file `window_frame.png` will override the game's `window_frame` asset).
- **Collision Prevention**: To resolve conflicts where different game assets share identical filenames (e.g., multiple `Default_00.png` portrait files across different character directories), KupoUI.PR supports **path-based overrides** using relative `GameAssets/` paths to target specific assets precisely.

### Folder Layout

The texture root is fixed to:

```
<GameRoot>/Modules/
```

Recommended structure created automatically on first run:

```
<GameRoot>/
  Modules/
    .cache/               ← auto-generated texture atlas cache
    00-Mods/              ← active custom mods
      Amano Style TitleScreen/
        ObjectConfig.json
        TitleLogoImage.png
        TitleLogoImage_EN.png
      FF2 DFFOO Portraits/
        SpeakerPortraits/
          SPEAKER_01.png   ← Firion
          SPEAKER_02.png   ← Maria
          SPEAKER_03.png   ← Guy
      FF2 Pixel Keeper/
      FF2 Total Hell/
    01-UI-Themes/         ← full UI theme packs
    02-UI-Frames/         ← UI frame texture packs
    03-UI-BgColor/        ← UI background color packs
    04-UI-Cursors/        ← cursor texture packs
    05-Button-Prompts/    ← button prompt texture packs
    System/               ← cross-game fonts & global configurations
      fontconfig.json     ← custom font mapping
      TextConfig-sample.json
      Icons/              ← custom inline text icons
      FF2/
        SpeakerPortraits/
          MenuPortraitMap.json ← character portrait mapping for FF2
```

Within each numbered folder you can create named sub-folders (packs). The active pack for each category is selected via the corresponding config key (e.g. `UIThemesFolder = Darker UI` selects `01-UI-Themes/Darker UI/`). An empty value means no pack is selected for that category.

The `System/` folder is auto-created on first run. Place textures that apply to all six games directly inside `System/`, or inside the matching game-tag sub-folder (e.g. `System/FF2/`) to target a specific game. Speaker portraits belong in `System/SpeakerPortraits/`.

> [!NOTE]
> **Migrating from `Shared/`**: The `Shared/` folder has been renamed to `System/`. If an existing `Modules/Shared/` folder is detected on startup, the plugin will automatically copy its contents into `Modules/System/` and log a warning. Rename the folder manually to suppress this message on future runs.

### Lookup Priority

Priority is highest to lowest:

1. `05-Button-Prompts/<ButtonPromptsFolder>`
2. `04-UI-Cursors/<CursorsFolder>`
3. `03-UI-BgColor/<UIBgColorFolder>`
4. `02-UI-Frames/<UiFramesFolder>`
5. `01-UI-Themes/<UIThemesFolder>`
6. `00-Mods/`
7. `00-Mods/<GameTag>/` (e.g. `00-Mods/FF2/`)
8. `00-Mods/System/`
9. `<GameTag>/` (root-level game-tag folder, if present)
10. `System/<GameTag>/` (e.g. `System/FF2/`)
11. `System/` (cross-game, lowest priority)

Use the file name **without extension** to match the in-game texture/sprite name (e.g. `window_frame.png` replaces the asset named `window_frame`).

### Ignoring / Game-Tag / Blocking Folders

- **Category Folder Scoping**: For category folders (`01-UI-Themes`, `02-UI-Frames`, `03-UI-BgColor`, `04-UI-Cursors`, `05-Button-Prompts`), configuration `.json` files (`ObjectConfig.json`, `TextConfig.json`, `IconsConfig.json`, `SpeakerNames.json`, `MenuPortraitMap.json`) are **only** loaded from the specified active pack folder (e.g. `UIThemesFolder = MyTheme`). Inactive pack folders under these 5 categories are skipped, preventing unselected themes or frames from conflicting with mods in `00-Mods/` or `System/`.
- **Blocked Folders**: If any directory/folder in a file's path starts with the word `block` (case-insensitive, e.g., `block-mod`, `blockUI`, `block_portraits`), the plugin will completely ignore and skip loading any files (textures, configs, portraits) from that folder and its subdirectories. Use this prefix to temporarily disable mods or assets without deleting them.
- **Game-Tag Folders**: If a configuration file (`SpeakerNames.json` / `speaker-names.json`, `MenuPortraitMap.json`, `ObjectConfig.json`, `TextConfig.json`, `IconsConfig.json`) or portrait image is located under a game-tag sub-folder (e.g., `FF1`, `FF2`, `FF3`, `FF4`, `FF5`, `FF6`) that does not match the game currently running, it will be skipped entirely.

### Path-Based Overrides

Many FFPR assets share the same file name across different bundles (e.g. `Default_00.png` used in multiple portrait folders). To avoid collisions, KupoUI.PR supports path-based resolution for files placed under a `GameAssets/` folder within any mod folder.

- If a replacement file path contains a `GameAssets/…` segment, it is indexed by its full relative path (no extension), not by name alone.
- At runtime, when the game loads an address like `Assets/GameAssets/…`, KupoUI.PR resolves to the exact matching replacement first.
- Name-only matching still works as a fallback.

**Example — FF2 portrait:**

- In-game address: `Assets/GameAssets/Serial/Res/Chara/Face/FA_FF2_P001/Default_00.png`
- Replacement file: `<GameRoot>/Modules/00-Mods/GameAssets/Serial/Res/Chara/Face/FA_FF2_P001/Default_00.png`

### Prefabs & Battle Background Support

> [!IMPORTANT]
> **Battle backgrounds in Final Fantasy Pixel Remaster are now fully replaceable and moddable without Bundle editing!**
> Since battle background assets are stored inside Unity `.prefab` containers, the plugin tracks these nested references at runtime. You can easily override background textures/sprites without needing to modify the `.prefab` assets directly.

If a texture/sprite is referenced inside a `.prefab` addressable, the mod tracks it and resolves it using three container-aware rules to keep your directories clean (note: runtime addresses omit the `.prefab` extension):

1. **Folder-Matching Containers:** If the texture name matches the containing directory name (e.g. texture `BG_FF4_01` inside `BG_FF4_01/BgPrefab`), the redundant parent directory and prefab segments are simplified. This makes replacing standard battle backgrounds extremely straightforward:
   - In-game address: `Assets/GameAssets/Serial/Res/Battle/Background/BG_FF4_01/BgPrefab` (texture: `BG_FF4_01`)
   - Replacement file: `<GameRoot>/Modules/00-Mods/GameAssets/Serial/Res/Battle/Background/BG_FF4_01.png`
2. **Generic Prefabs:** If the prefab file is a generic wrapper named `BgPrefab`, the `BgPrefab` segment is omitted:
   - In-game address: `Assets/GameAssets/Serial/Res/Battle/Background/BG_FF4_01/BgPrefab` (texture: `BG_FF4_01_diffuse`)
   - Replacement file: `<GameRoot>/Modules/00-Mods/GameAssets/Serial/Res/Battle/Background/BG_FF4_01/BG_FF4_01_diffuse.png`
3. **Standard Prefabs:** For standard nested prefabs, the prefab name is included in the subfolder namespace to prevent texture name collisions:
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
  "wrapMode": "Repeat",
  "wrapModeU": "Repeat",
  "wrapModeV": "Repeat",
  "pivot": "0.5,0.5",
  "border": "0,0,0,0",
  "rectX": 0,
  "rectY": 16,
  "flipHorizontal": false,
  "preserveAspect": true,
  "scale": 1.0,
  "offsetX": 0.5,
  "offsetY": -0.8,
  "spriteWidth": 36,
  "spriteHeight": 56,
  "spriteRectX": 0,
  "spriteRectY": 0,
  "spriteOffsetX": 0.0,
  "spriteOffsetY": -28.0
}
```

| Field                                         | Description                                                                                                              |
| --------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------ |
| `width`                                       | Logical source width used to calculate replacement sprite scale.                                                         |
| `height`                                      | Logical source height used to calculate replacement sprite scale.                                                        |
| `pixelsPerUnit`                               | Direct sprite PPU override (takes priority over auto scale calculation).                                                 |
| `filterMode` / `filterType`                   | Filter override: `Point`, `Bilinear` (or `Linear` alias), or `Trilinear`. String mode takes priority over `pointFilter`. |
| `pointFilter`                                 | Legacy boolean shorthand: `true` = `Point`, `false` = `Bilinear`.                                                         |
| `wrapMode` / `wrap_mode`                      | Global wrap mode: `Clamp`, `Repeat`, `Mirror`, `MirrorOnce`. Default: `Clamp`.                                           |
| `wrapModeU` / `wrapModeX` / `wrap_mode_u/x`   | Horizontal (U/X axis) wrap mode override: `Clamp`, `Repeat`, `Mirror`.                                                   |
| `wrapModeV` / `wrapModeY` / `wrap_mode_v/y`   | Vertical (V/Y axis) wrap mode override: `Clamp`, `Repeat`, `Mirror`.                                                     |
| `repeatX` / `repeat_x`                        | Boolean shorthand for horizontal tiling: `true` = `"Repeat"`, `false` = `"Clamp"`.                                       |
| `repeatY` / `repeat_y`                        | Boolean shorthand for vertical tiling: `true` = `"Repeat"`, `false` = `"Clamp"`.                                         |
| `pivot`                                       | Normalized sprite anchor `"x,y"` (0–1). E.g. `"0.5,0.5"` = center, `"0,0"` = bottom-left.                                 |
| `border`                                      | 9-slice border in pixels `"left,bottom,right,top"`. Use `"0,0,0,0"` to strip an inherited border.                         |
| `rectX`                                       | Pixel X offset within the replacement texture (source UV position, not screen position).                                 |
| `rectY`                                       | Pixel Y offset within the replacement texture. Useful for sprite sheets.                                                 |
| `flipHorizontal` / `flipX`                    | Flip the replacement texture horizontally.                                                                               |
| `preserveAspect`                              | Bilinear-scale custom sprite to best-fit inside the original bounding rect, padded with transparency. Prevents cropping. |
| `scale`                                       | Extra multiplier applied on top of the `preserveAspect` best-fit (e.g. `0.8` = 80%, `1.5` = 150%).                       |
| `offsetX`                                     | Shift custom sprite renderer position inside the battle frame horizontally (positive = right, negative = left).          |
| `offsetY`                                     | Shift custom sprite renderer position inside the battle frame vertically (positive = up, negative = down).               |
| `spriteWidth`                                 | Logical display width for sprite-specific layout (takes precedence over `width` in UI and Bestiary screens).             |
| `spriteHeight`                                | Logical display height for sprite-specific layout (takes precedence over `height`).                                     |
| `spriteRectX`                                 | Pixel X offset within the replacement texture specific to the sprite rect.                                                |
| `spriteRectY`                                 | Pixel Y offset within the replacement texture specific to the sprite rect.                                                |
| `spriteOffsetX`                               | Sizing layout offset X (takes precedence over `offsetX`).                                                                 |
| `spriteOffsetY`                               | Sizing layout offset Y (takes precedence over `offsetY`).                                                                 |

> **Automatic UI Texture Tiling:** When `wrapMode` (or per-axis U/V wrap modes) is set to `Repeat` or `Mirror` in sidecar metadata, KupoUI.PR automatically configures `UnityEngine.UI.Image` to `Image.Type.Tiled` and recalculates `uvRect` for `RawImage` components at render time (`OnPopulateMesh`). Outer 9-slice borders are cleared to `0,0,0,0` unless an explicit `border` inset is declared in metadata, preventing edge-stretching artifacts. Native game UI elements (e.g. save slot thumbnails, UI masks) without custom tiling sidecars are protected and rendered normally.

### Texture Formats & Filter Modes

**Supported formats:** `png`, `jpg`, `jpeg`, `tga`, `dds` (DXT1, DXT5, uncompressed RGBA32. Note: DDS textures require `Utility.EnableDDSTextures = true` in your BepInEx config).

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

## 2. 📐 GameObject & Layout Engine (ObjectConfig.json)

Manipulate Unity GameObjects at runtime (position, rotation, scale, active state, text properties) without writing C# — just drop an `ObjectConfig.json` file anywhere inside `Modules/` (`00-Mods/`, `System/`, or your active category pack folder).

The plugin scans `ObjectConfig.json` files under `Modules/` on startup. Configuration files inside category folders (`01-UI-Themes`–`05-Button-Prompts`) are loaded **only** from the currently active pack folder. Files placed inside `System/FF1`–`FF6` sub-folders are filtered to the detected game, so only the matching game's rules are applied.

### Folder Placement

```
<GameRoot>/
  Modules/
    00-Mods/
      Amano Style TitleScreen/
        ObjectConfig.json   ← picked up
    01-UI-Themes/
      Darker UI/
        ObjectConfig.json   ← picked up (only if UIThemesFolder = Darker UI)
      Demake UI/
        ObjectConfig.json   ← skipped (not active theme)
    System/
      ObjectConfig.json     ← picked up (applies to all games)
      FF2/
        ObjectConfig.json   ← picked up only when running FF2
```

### File Format

```json
{
  "objects": [
    {
      "TargetObjectName": "main_menu",
      "TargetPath": "RootObject/menu_canvas/ui_root/notch_root/title(Clone)/content_root/menu/main_menu",
      "Position": { "x": -730, "y": 580, "z": 0 },
      "Scale": { "x": 0.9, "y": 0.9, "z": 1.0 }
    },
    {
      "TargetObjectName": "last_text",
      "TargetPath": "RootObject/menu_canvas/ui_root/notch_root/title(Clone)/content_root/start/last_text",
      "FontSize": 40,
      "TextColorWhite": true,
      "DisableShadow": true
    }
  ]
}
```

The `objects` array can contain as many entries as you need, spread across one file or multiple files in different mod folders.

### Fields

| Field                  | Required | Description                                                                                                                                                                                                                                                                                                                                                           |
| ---------------------- | -------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `TargetObjectName`         | **Yes**  | Exact`GameObject` name to match (e.g. `"menu_base(Clone)"`).                                                                                                                                                                                                                                                                                                          |
| `TargetPath`               | No       | Hierarchy path suffix to disambiguate objects with the same name. Forward-slash notation, matched from the object upward. E.g.`"Canvas/aspect_parent/menu_base(Clone)"`.                                                                                                                                                                                              |
| `SceneName`                | No       | Only apply this rule while this scene is active. Omit to apply in every scene. Case-insensitive.                                                                                                                                                                                                                                                                      |
| `Position`                 | No       | Sets`transform.localPosition`. Provide `x`, `y`, `z` as floats.                                                                                                                                                                                                                                                                                                       |
| `Rotation`                 | No       | Sets`transform.localEulerAngles` (Euler angles in degrees). Provide `x`, `y`, `z`.                                                                                                                                                                                                                                                                                    |
| `Scale`                    | No       | Sets`transform.localScale`. Provide `x`, `y`, `z`.                                                                                                                                                                                                                                                                                                                    |
| `Size`                     | No       | Sets absolute width and height on`UnityEngine.RectTransform` component (if present) via `SetSizeWithCurrentAnchors`. Provide `x` (width) and `y` (height) floats.                                                                                                                                                                                                     |
| `SetActive`                | No       | Calls`gameObject.SetActive(value)`. Use `true` or `false`.                                                                                                                                                                                                                                                                                                            |
| `TextAlignment`            | No       | Sets`Text.alignment` on the `UnityEngine.UI.Text` component (if present). See [Text Alignment Values](#text-alignment-values).                                                                                                                                                                                                                                        |
| `ChildAlignment`           | No       | Sets`LayoutGroup.childAlignment` on the `UnityEngine.UI.LayoutGroup` component (if present, e.g. horizontal/vertical/grid layouts). See [Text Alignment Values](#text-alignment-values).                                                                                                                                                                              |
| `FontSize`                 | No       | Sets`Text.fontSize` on the `UnityEngine.UI.Text` component (if present). Provide an integer.                                                                                                                                                                                                                                                                          |
| `ResizeTextForBestFit`     | No       | Sets`Text.resizeTextForBestFit`. Use `true` or `false`.                                                                                                                                                                                                                                                                                                               |
| `ResizeTextMaxSize`        | No       | Sets`Text.resizeTextMaxSize`. Provide an integer.                                                                                                                                                                                                                                                                                                                     |
| `ResizeTextMinSize`        | No       | Sets`Text.resizeTextMinSize`. Provide an integer.                                                                                                                                                                                                                                                                                                                     |
| `TextColorWhite`           | No       | Legacy shortcut to force`Text.color` to white. Use `Color` for custom colors.                                                                                                                                                                                                                                                                                         |
| `Color`                    | No       | Forces`Graphic.color` on `UnityEngine.UI.Graphic` components (`Text`, `Image`, `RawImage`). Re-enforced on every color write to prevent game overrides. Accepts Hex string (e.g. `"#FF5500"`, `"#FF5500FF"`), color name, or RGBA object (`{"r": 1.0, "g": 0.5, "b": 0.0, "a": 1.0}`). See [Supported Color Names](#supported-color-names) for a list of valid names. |
| `DisableShadow`            | No       | Disables all`UnityEngine.UI.Shadow` components on the matching GameObject. Use `true`.                                                                                                                                                                                                                                                                                |
| `DisableMask`              | No       | Disables all`UnityEngine.UI.Mask` and `UnityEngine.UI.RectMask2D` components on the matching GameObject. Use `true`.                                                                                                                                                                                                                                                  |
| `IgnoreLayout`             | No       | Adds/sets a `UnityEngine.UI.LayoutElement` with `ignoreLayout = true` to prevent parent `LayoutGroups` (like `VerticalLayoutGroup`) from overriding this object's position. Use `true`.                                                                                                                                                                                |
| `DisableContentSizeFitter` | No       | Disables all `UnityEngine.UI.ContentSizeFitter` components on the matching GameObject. Use `true`.                                                                                                                                                                                                                                                                    |
| `DisableLayoutGroup`       | No       | Disables all `UnityEngine.UI.LayoutGroup` components on the matching GameObject. Use `true`.                                                                                                                                                                                                                                                                          |
| `DisableLayoutElement`     | No       | Disables all `UnityEngine.UI.LayoutElement` components on the matching GameObject. Use `true`.                                                                                                                                                                                                                                                                        |
| `SiblingIndex`             | No       | Sets the sibling layout order/depth index in the hierarchy (e.g. `0` to move the object to the very back, or `-1` to bring it to the very front). Provide an integer.                                                                                                                                                                                                   |
| `NewImages`                | No       | A list of custom image UI elements to instantiate and parent under this GameObject. See [Inserting Custom Image Objects](#inserting-custom-image-objects) below.                                                                                                                                                                                                         |

> **Note:** All fields except `TargetObjectName` are optional. Only include the ones you want to change — unspecified fields leave the object unchanged.

### Supported Color Names

When using a string for the `Color` field, you can use standard color names or the game's built-in UI text color presets:

#### Standard Colors

- `white`, `black`, `red`, `green`, `blue`, `yellow`, `cyan`, `magenta`, `gray` (or `grey`), `clear`
- `navy` (`#000080`), `crimson` (`#DC143C`), `violet` (`#EE82EE`), `orange` (`#FFA500`)

#### Native Game Colors

These map directly to the game's built-in `Last.UI.TextColors` palette:

- `resuscitationyellow`
- `keyhelpblack`
- `timestampblue`
- `lightblue`
- `game_white` (or `gamewhite`)
- `game_black` (or `gameblack`)
- `game_yellow` (or `gameyellow`)
- `game_blue` (or `gameblue`)
- `game_gray` (or `game_grey` / `gamegray` / `gamegrey`)
- `game_red` (or `gamered`)
- `game_green` (or `gamegreen`)

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
> - **Index-based sibling targeting (`Name[index]`)**: If multiple objects have the exact same name under the same parent, you can append a 0-based index to any path segment, e.g. `TargetPath: "parent_object/child_object[1]"`. The index is computed specifically among siblings that share that name (e.g., `[0]` is the first sibling with that name, `[1]` is the second sibling with that name, etc.).

### Disabling a Mask on a Specific Object

If you want to disable a mask on a specific element that might share a generic name with others (like `viewport` or `Mask`) under the same parent, combine `TargetPath` with an index and `DisableMask`:

```json
{
  "TargetObjectName": "viewport",
  "TargetPath": "Canvas/aspect_parent/menu_parent/scroll_view/viewport[0]",
  "DisableMask": true
}
```

### Hiding an Object

```json
{
  "TargetObjectName": "some_ui_element",
  "SceneName": "MainMenu",
  "SetActive": false
}
```

> **Note on `SetActive: false` behaviour:** The rule uses a Harmony prefix that intercepts every `SetActive(true)` call and flips it to `false` before Unity processes it. This permanently prevents the object from becoming active — no flicker, no one-frame delay.

### Inserting Custom Image Objects

You can instantiate and insert new custom image objects (with standard Unity UI `Image` components) as children of a targeted UI element using the `NewImages` property. 

The image file path is resolved **relative to the `ObjectConfig.json` file** itself. If `Size` is omitted, the image will default to its natural pixel dimensions.

> [!TIP]
> **Sidecar Metadata Support**: Just like standard texture overrides, you can drop a sidecar `.json` metadata file next to your custom image (e.g., `my_images/badge.json` next to `my_images/badge.png`) to define custom **9-slicing borders**, **pivots**, **pixelsPerUnit**, filter modes, and texture sub-rects.

```json
{
  "TargetObjectName": "menu_base(Clone)",
  "NewImages": [
    {
      "Name": "custom_badge",
      "ImagePath": "my_images/badge.png",
      "Position": { "x": 120, "y": -45, "z": 0 },
      "Size": { "x": 64, "y": 64 },
      "Color": "#FFFFFF"
    }
  ]
}
```

#### Fields inside `NewImages`

| Field | Required | Description |
| --- | --- | --- |
| `Name` | **Yes** | The name of the new child GameObject to create. If an object with this name already exists as a child, it will be updated/re-used rather than duplicated. **Supports path-like names** (e.g., `"container_name/image_name"`) to automatically create intermediate empty container GameObjects (which automatically ignore parent layout groups). |
| `ImagePath` | **Yes** | Path to the image file (supporting `.png`, `.jpg`, `.jpeg`, `.tga`, or `.dds`), relative to the directory of the `ObjectConfig.json`. |
| `Position` | No | Sibling-local position offset (`x`, `y`, `z`). |
| `Rotation` | No | Euler rotation angles (`x`, `y`, `z`). |
| `Scale` | No | Sibling-local scale factors (`x`, `y`, `z`). Defaults to `1.0` if omitted. |
| `Size` | No | Width (`x`) and height (`y`) bounds. Defaults to the image's natural dimensions if omitted. |
| `Color` | No | Color tint overlay to apply to the image component. Supports Hex strings or RGBA objects. |
| `ImageType` | No | Sets the render type for the Unity UI Image component. Accepted values (case-insensitive): `Simple`, `Sliced`, `Tiled`, `Filled`. Defaults to `Sliced` if a border is present, or `Simple` if not. |
| `SiblingIndex` | No | Sets the sibling draw/layout depth order of the new image (e.g., `0` to place it at the very back behind other children, or `-1` to place it at the very front). |
| `IgnoreLayout` | No | Adds/sets a `UnityEngine.UI.LayoutElement` with `ignoreLayout = true` on the new image to prevent parent `LayoutGroups` from overriding its position. Use `true`. |
| `DisableContentSizeFitter` | No | Disables any `UnityEngine.UI.ContentSizeFitter` component on the new image object. Use `true`. |
| `DisableLayoutGroup` | No | Disables any `UnityEngine.UI.LayoutGroup` component on the new image object. Use `true`. |
| `DisableLayoutElement` | No | Disables any `UnityEngine.UI.LayoutElement` component on the new image object. Use `true`. |

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

### Scaled-Down Menu

`UI.ScaledDownMenu` (default `true`) — Shrinks the in-game menu by 10% by setting `localScale` to `(0.9, 0.9, 1.0)` on:

- `Canvas/aspect_parent/menu_parent/menu_base(Clone)`
- `RootObject/sab_canvas/root/ui_root`

### Save Highlight Color

`UI.SaveHighlightColor` (default `Disable`) — Overrides the Quick Save and Auto Save slot highlight color.

- **Options**: `Original` (game default), `DarkNavy`, `DarkGreen`, `DarkViolet`, `DarkYellow`, `DarkOrange`, `Disable`.
- **Disable Aliases**: You can also use `Disabled`, `Off`, or `None` to disable the highlight slot entirely.
- **Fallback Behavior**: If an unrecognized value is set, the color defaults to `DarkNavy` to ensure deterministic rendering.

### Title Screen & Main Menu Background Images

#### Title Screen Background Color

`UI.TitleScreenBgColor` — Controls the color of the title screen's solid background panel.

Options: `original` (game default), `white`, `black`, `navy`, `crimson`, `violet`.

The patch intercepts the `Graphic.color` setter and re-enforces the chosen color on every material update to prevent the game from overriding it.

#### Title Screen Full Background Image

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
- For best results, use an image sized to your target resolution (e.g. 1920×1080).

##### Custom Title Logo Image Overrides
When using custom full-screen background images, you may want to hide or override the game's built-in title logos. To do this, replace the following three texture files with completely transparent PNG files in your mod folder:
- `TitleLogoImage`
- `TitleLogoImage_EN`
- `TitleLogoImage_ZH-CH`

> [!NOTE]
> The game's main title logos are tied directly to the fade-in animation timeline sequence on startup, making them extremely difficult to cleanly patch via Harmony. Replacing these files with transparent textures on disk is the recommended way to hide or override them.

#### Main Menu Background Image

Drop any supported image named `MainMenuBg` into any mod folder to inject a custom background image behind the main menu. No config entry is required — if the file is absent, nothing happens.

```
<GameRoot>/Modules/00-Mods/MyMod/MainMenuBg.png
```

**Supported formats:** `png`, `jpg`, `jpeg`, `tga`, `dds`

**How it works:**

The patch watches for the main menu's `menu_parent` container or `menu_base(Clone)` object under:

```
Canvas/aspect_parent/menu_parent
```

When either is activated, a new `MainMenuBgObject` `RawImage` GameObject is injected as a sibling of `menu_parent` immediately behind it (lower sibling index), stretched to fill the parent `aspect_parent` rect. The background automatically mirrors the active state of `menu_parent` and is cleaned up when `menu_parent` is destroyed.

---

## 5. 📝 Text & Inline Icon Engine

Like `ObjectConfig.json`, you can place files named `TextConfig.json` under the `Modules/` directory (`00-Mods/`, `System/`, or inside your active category pack folder). They are parsed additively at startup to override in-game menu texts, buttons, names, and dialogs.

This is highly useful for:
- **Partial Re-translations**: Safely swap specific dialogue lines or interface text database-wide without needing full language localization files or bundle-packing.
- **Menu Option Renames**: Modify or customize specific menu options, labels, or screen headers (e.g. renaming "Item" to "Bag" or "Status" to "Stats") by targeting their database keys or active UI GameObject hierarchies.

### File Format

`TextConfig.json` files support:

1. **`Language`**: (Optional) Declared at the file root level to scope all rules inside this file to a specific game language.
2. **`texts`**: A simple key-value dictionary (`"Key": "ReplacementText"`) to quickly replace localization strings by their database ID (e.g. `MSG_SYSTEM_002`) or original string.
3. **`objects`**: An array of GameObject override rules to replace the text of specific UI elements by their path/name hierarchy.

#### Object Fields

Each entry inside the `"objects"` array supports:

| Field                | Required | Description                                                                                                                                             |
| -------------------- | -------- | ------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `"TargetObjectName"` | No\*     | Exact name of the target `GameObject` to match (e.g., `"value_text"`). _Either `TargetObjectName` or a key mapping is required to identify the target._ |
| `"TargetPath"`       | No       | Transform hierarchy path suffix to uniquely identify the GameObject (e.g., `"LocationParent/location/value_text"`).                                     |
| `"SceneName"`        | No       | Restricts this rule to a specific active scene (case-insensitive).                                                                                      |
| `"OriginalText"`     | No       | If set, the rule is only applied if the target's current text matches this string.                                                                      |
| `"NewText"`          | **Yes**  | The replacement text string to write.                                                                                                                   |

#### Example `TextConfig.json`

```json
{
  "Language": "Ja",
  "texts": {
    "MSG_SYSTEM_002": "HHELLO WORLD",
    "MSG_SYSTEM_022": "戻る",
    "Confirm": "はい"
  },
  "objects": [
    {
      "TargetObjectName": "value_text",
      "TargetPath": "LocationParent/location/value_text",
      "SceneName": "Title",
      "OriginalText": "Tower of Worship",
      "NewText": "別のテキスト"
    }
  ]
}
```

#### How to Find Text Paths and Keys (Diagnostics)

To easily identify the correct paths and localization keys for your `TextConfig.json` modifications:

1. Open `BepInEx/config/faospark.kupoui.pr.cfg`.
2. Enable `Z - Diagnostics` -> `LogAllTexts`:
   ```ini
   LogAllTexts = true
   ```
3. Boot the game and navigate to the screens you wish to mod.
4. Check BepInEx console output or `BepInEx/LogOutput.log` for logs prefixed with `[TextLog]`:

   ```
   [Info   :KupoUI.PR] [TextLog] Path: '../ui_root/location_text' | Key: 'MSG_SYSTEM_002' | Value: 'Go Back!'
   ```

   - Use the **Path** value for `TargetPath` (strip the leading `../` segment).
   - Use the **Key** value for the `"texts"` key-value dictionary.
   - Use the **Value** for `"OriginalText"` filtering.

### Language & Game Scoping

You can limit configuration files (`TextConfig.json`, `ObjectConfig.json`, and `IconsConfig.json`) to a specific language and/or game by adding `"Language"` and/or `"GameTag"` parameters at the root level:

```json
{
  "GameTag": "FF1",
  "Language": "Ja",
  "texts": {
    "Confirm": "はい"
  }
}
```

- If `"GameTag"` (e.g. `"FF1"`, `"FF2"`, `"FF3"`, `"FF4"`, `"FF5"`, `"FF6"`) is specified, the file will only be loaded when running that specific game.
- If the `"Language"` parameter is set, the overrides will only apply when that language is active in the game's settings.
- Both scopes are optional and can be combined.

Supported language values:

- `En` (English)
- `Ja` (Japanese)
- `Fr` (French)
- `De` (German)
- `It` (Italian)
- `Ru` (Russian)
- `Pt` (Portuguese)
- `Th` (Thai)
- `Ko` (Korean)
- `Zht` (Traditional Chinese)
- `Zhc` (Simplified Chinese)

---

## IconsConfig.json — Custom Rich Text Inline Icons

You can define custom icon tags (e.g. `<IC_BAG>`, `<IC_ARMOR>`, `<IC_CUSTOM>`) in your `TextConfig.json` or in dialogue strings, and map them to custom PNG files. The plugin will automatically parse these tags in standard `Text` components, replace them with spacers, and dynamically render custom sprites on-screen.

### File Format

Create `IconsConfig.json` inside the `Modules/System/` folder. The file maps the tag name to the filename of the PNG file located inside `Modules/System/Icons/`:

```json
{
  "IC_BAG": "bag.png",
  "IC_ARMOR": "armor.png",
  "IC_CUSTOM": "my_icon.png"
}
```

- **Sprites Location**: Save the referenced `.png` sprite files under `Modules/System/Icons/` (e.g., `Modules/System/Icons/bag.png`).
- **Sizing**: Sprites are rendered at `12x12` pixels in size.
- **Vertical Alignment**: Icons are automatically offset vertically relative to the text line's baseline to align beautifully with the characters.

### Language-Agnostic Icon Injection (e.g. for Inventory Items)

Normally, overriding an item name to add a custom icon requires specifying the full name in your config (which is language-bound):

```json
  "texts": {
    "MSG_ITEM_NAME_34": "<IC_ETHER>Ether"
  }
```

To make icon injection language-agnostic, you can configure the value to consist **solely of the icon tag**:

```json
  "texts": {
    "MSG_ITEM_NAME_34": "<IC_ETHER>"
  }
```

When an override consists of only a tag (e.g., `<IC_ETHER>`), the plugin will automatically strip any existing native icon tag from the in-game text and prepend your custom icon tag while **preserving the original translated item name**. This allows you to apply custom icons globally without redefining item names for every language.

### Performance & Texture Atlases

Because inline text icons are rendered frequently (especially when scrolling lists like the inventory or magic menus), loading and drawing them as individual textures can cause severe performance lag and high draw call overhead.

To solve this, **KupoUI.PR automatically packs all registered custom icons into unified texture atlases at startup**:

- **Automatic Grouping**: Icons are split into a **point-filter** atlas (for pixel art / textures ≤ 32px) and a **bilinear-filter** atlas (for high-resolution icons) to ensure optimal filtering.
- **Batch Rendering**: Draw calls are merged by Unity since all sprites share the same underlying atlas textures, enabling smooth 60+ FPS scrolling.
- **Startup Caching**: The generated atlases and a layout index are written to a `.cache/` folder under your active `Modules/` directory:
  - `Modules/.cache/icons_atlas_<GameTag>_point.png`
  - `Modules/.cache/icons_atlas_<GameTag>_linear.png`
  - `Modules/.cache/icons_atlas_<GameTag>.json`
- **Instant Loading**: Subsequent game launches load the pre-packed atlas PNGs and layout metadata directly (taking only a few milliseconds) and bypass reading individual icon PNGs.
- **Smart Invalidation**: The index saves the exact file modification timestamps of all source icons. If you edit, add, or remove any icon or config file, the cache is automatically invalidated and rebuilt on the next startup. You can also delete the `.cache/` folder to force a clean rebuild.
- **Seamless Fallback**: If atlas creation fails for any reason (e.g., Unity engine or platform constraint), the plugin gracefully falls back to using individual sprites.

### Disable Item Dimming

`UI.DisableItemDimming` (default `false`) — Forces all item list icons and text labels (in main menu item lists, battle item/info lists, and shop views) to display at full color, ignoring the gray dim tint applied by the game to unusable or un-equipable items.

---

## 3. 💬 Dialogue & Portrait Engine

### Speaker Name Prefix

`UI-Dialog.MessageSpeakerPrefix` (default `true`) — Prepends the speaker's name to the dialogue message text inside the message window, without modifying any game files.

- `UI-Dialog.SpeakerNameUppercase` (default `false`) — Transform the speaker name to UPPERCASE before prepending.
- `UI-Dialog.SpeakerNameNewLine` (default `false`) — Inserts a line break (new line) after the speaker prefix in dialogue boxes to prevent text overflow.
- `UI-Dialog.DialogueTextWrap` (default `true`) — Forces built-in text wrapping on dialogue text boxes to prevent horizontal overflow.
- `UI-Dialog.DialogueLineLengthLimit` (default `0`) — If greater than 0, forces dialogue text to wrap at this maximum character count per line. Useful when prepending speaker names to prevent text overflow.
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

- **Portrait Directory Requirement**: Place portrait image files (`.png`, `.jpg`, `.dds`, etc.) inside a folder named `SpeakerPortraits/` (e.g., `Modules/System/SpeakerPortraits/` or `Modules/<ModFolder>/SpeakerPortraits/`). The `Modules/System/SpeakerPortraits/` directory is created automatically on first run. Portraits are resolved using the speaker ID (e.g., `SPEAKER_77.png`) or display name (e.g., `Cecil.png`).
- `UI-Dialog.FlipSpeakerPortraits` (default `true`) — Flip all injected portraits horizontally.
- `UI-Dialog.SpeakerPortraitsPadding` (default `0,0,0,0`) — Offset padding `left,top,right,bottom` in pixels to shrink and shift the injected portrait container.
- `UI-Dialog.SpeakerPortraitsTextOffset` (default `0`) — Offset (in pixels) for the dialogue text box (`lastText`) when speaker portraits are active. Supports `X` or `X,Y` format (e.g., `-75` or `-75,10`). Positive X moves right, positive Y moves up.
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
  System/
    SpeakerPortraits/
      MenuPortraitMap.json         ← recommended location
```

#### Mapping Format

Inside `MenuPortraitMap.json`, define key-value pairs where the key is the menu portrait address or Speaker ID, and the value is the target Speaker ID/name or portrait image filename:

```json
{
  "FA_FF2_P001": "SPEAKER_01",
  "FA_FF2_P002": "SPEAKER_02",
  "FA_FF2_P003": "SPEAKER_03",
  "FA_FF2_P004": "SPEAKER_07",
  "FA_FF2_P005": "SPEAKER_23"
}
```

#### Language & Game Scoping

You can limit portrait overrides to a specific language and/or game by adding `"Language"` and/or `"GameTag"` properties at the root of `MenuPortraitMap.json`:

```json
{
  "GameTag": "FF4",
  "Language": "Ja",
  "P003": "Rydia"
}
```

- If `"GameTag"` (e.g. `"FF1"`, `"FF2"`, `"FF3"`, `"FF4"`, `"FF5"`, `"FF6"`) is specified, the file will only be loaded when running that specific game.
- If `"Language"` is specified, the mappings in this file will only apply when playing the game in that language.
- Both scopes are optional and can be combined.

If mapped to a dialogue Speaker ID (like `SPEAKER_05`), the plugin will automatically resolve its display name from `speaker-names.json` (e.g., `"SPEAKER_05": "Kain"`) and search the BepInEx `SpeakerPortraits/` folders for either `SPEAKER_05.png` or `Kain.png`.

#### Zero-Config Fallback (No JSON mapping needed)

If no mapping is defined in `MenuPortraitMap.json`, the plugin automatically falls back to searching for a custom portrait matching:

1. The full speaker ID (e.g. `FA_FF4_P001.png`)
2. The shorthand ID (e.g. `P001.png`)
3. The display name override in `SpeakerNames.json` / `speaker-names.json` (if any exists for that ID)

#### Menu Portrait Aspect Ratio Preservation

Automatically preserves the aspect ratio of custom character portraits displayed on the main menu screen (by setting `preserveAspect = true` and bypassing the default `SetNativeSize` execution on the `/chara_rect/front/front_parent/charac_parent/chara_image` UI Image component). This ensures that custom high-resolution character portraits do not stretch or distort.

### Speaker Name Overrides

`SpeakerNames.json` (or `speaker-names.json` for compatibility) lets you register speaker IDs with display names and override speaker identity on a per-dialogue-key basis — all without touching game files.

#### File Location

`SpeakerNames.json` or `speaker-names.json` can be placed in **any sub-folder under `Modules/`** — the plugin scans all of them recursively and merges every file it finds.

```
<GameRoot>/Modules/
  System/
    SpeakerPortraits/
      SpeakerNames.json         ← recommended filename and location
    FF2/
      SpeakerNames.json         ← game-specific (only used when running FF2)
  00-Mods/
    Amano Style TitleScreen/
      SpeakerNames.json         ← mod-specific
  01-UI-Themes/
    Darker UI/
      SpeakerNames.json         ← inside active theme pack
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

#### Language & Game Scoping

You can limit speaker and message overrides to a specific language and/or game by adding `"Language"` and/or `"GameTag"` properties at the root of `SpeakerNames.json`:

```json
{
  "GameTag": "FF2",
  "Language": "Ja",
  "speakers": {
    "SPEAKER_81": "Dark Knight"
  }
}
```

- If `"GameTag"` (e.g. `"FF1"`, `"FF2"`, `"FF3"`, `"FF4"`, `"FF5"`, `"FF6"`) is specified, the file will only be loaded when running that specific game.
- If `"Language"` is specified, the overrides in this file will only apply when playing the game in that language.
- Both scopes are optional and can be combined.

#### `speakers` — Register speaker IDs

Maps a speaker ID to a display name. **Always applied** when that speaker is active — overrides whatever name the game provides (not just a fallback for blank names).

| Key                                    | Value                        |
| -------------------------------------- | ---------------------------- |
| Internal speaker ID (e.g.`SPEAKER_77`) | Display name (e.g.`Crewman`) |

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

## 4. 🔤 Font & Typography Engine (fontconfig.json)

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
<GameRoot>/Modules/System/
  fontconfig.json         ← your active font configuration
  font-help.txt           ← auto-generated help guide (contains baseline defaults at the bottom)
```

Both files are created automatically on first startup.

#### Configuration File Format

The mapping file supports both **simple string values** and **object-based values**.

```json
{
  "En": {
    "Font01": { "FontName": "Dissidia", "LineSpace": 0.90, "YOffset": 4 },
    "Font07": { "FontName": "Dissidia", "LineSpace": 0.90, "YOffset": 4 },
    "Font08": { "FontName": "pix Chicago", "FontSize": 26, "LineSpace": 1.2, "YOffset": 4 },
    "Font10": { "FontName": "pix Chicago", "FontSize": 26, "LineSpace": 1.2, "YOffset": 4 }
  }
}
```

| Field       | Description                                                                                      |
| ----------- | ------------------------------------------------------------------------------------------------ |
| `FontName`  | Font family name (e.g.`"Segoe UI"`, `"Consolas"`). Required.                                     |
| `LineSpace` | Line height factor (e.g.`1.2`). Adjust if your font appears cramped or overflows dialogue boxes. |
| `YOffset`   | Vertical offset in pixels (e.g.`2.0` to adjust upward, `-1.5` to adjust downward). Optional.     |

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

##### Note for Linux & Steam Deck Users (via Proton)

To use custom system fonts when running the game on Linux or Steam Deck via Proton:

1. Locate your game's Wine prefix (compatdata directory), e.g.:
   `.../SteamApps/compatdata/<AppID>/pfx/drive_c/windows/Fonts/`
   _(Where `<AppID>` is the Steam Application ID of the specific Pixel Remaster game, e.g. `377840` for Final Fantasy II)_.
2. Copy your custom `.ttf` or `.otf` file into the `Fonts` directory inside the prefix.
3. In `fontconfig.json`, configure `"FontName"` using the exact **Font Family Name** of the font (e.g. `"Segoe UI"`), not the file name.

---

## 6. 🛠️ Logging & Utility Engine

### Startup Mod Loader & Texture Conflict Detection

At startup, the plugin automatically scans `00-Mods/` and logs a summary of every active mod:

```
[Info   :KupoUI.PR] [ModLoader] Loaded 2 mod(s) from 00-Mods: FF2 Pixel Keeper, DarkerUI
```

It then performs a cross-source texture conflict analysis and warns whenever multiple distinct sources (Mod A vs. Mod B, or a mod vs. `System/`) provide a texture that resolves to the exact same key:

- **Loose files** (not under `GameAssets/`) are compared by normalized filename without extension.
- **`GameAssets/` files** are compared only by their full addressable path key (e.g. `GameAssets/Serial/Res/Chara/Face/FA_FF2_P001/Default_00`). This means multiple different character subfolders within the same mod never trigger false positives.

```
[Warning:KupoUI.PR] [ModLoader] Texture conflict detected for 'window_frame':
[Warning:KupoUI.PR]   - [Mod 'DarkerUI'] 00-Mods\DarkerUI\window_frame.png
[Warning:KupoUI.PR]   - [Mod 'FF2 Pixel Keeper'] 00-Mods\FF2 Pixel Keeper\window_frame.png
```

This check runs automatically at every startup and hot-reload, so you always see an up-to-date conflict report.

> [!NOTE]
> **System Speaker Portraits Exclusion**: Bundled base speaker portraits located under `System/` (such as `System/FF2/SpeakerPortraits/` or `System/SpeakerPortraits/`) are intentionally excluded from texture conflict detection. Because mods placed in `00-Mods/` are designed to override default system portraits, this exclusion prevents false-positive conflict warning logs while ensuring genuine mod-vs-mod conflicts (e.g. Mod A vs Mod B) are still highlighted.

### Utility Settings

#### Disable Mouse Cursor

`Utility.DisableMouseCursor` (default `false`) — Hides the OS mouse cursor inside the game window using the Unity `Cursor` API.

#### Force VSync

`Utility.ForceVSync` (default `false`) — Forces `QualitySettings.vSyncCount = 1` and `Application.targetFrameRate = -1` on startup, and intercepts any game writes that would override these values.

### Developer Diagnostic Logging Modes

Dedicated diagnostic toggles available under `Z - Diagnostics` in the configuration file (`BepInEx/config/faospark.kupoui.pr.cfg`):
- `TextureLogger`: Texture discovery (`Discoveries`), resolution (`Resolutions`), and miss (`Misses`) logging modes.
- `LogFontMapping`: Log font parameter and instance details to identify `FontType` mappings.
- `MessageSpeakerPrefixLogging`: Log dialogue speaker name replacements and matches.
- `LogAllTexts`: Log all assigned `UnityEngine.UI.Text` strings to trace UI paths and keys.
- `IconLogging`: Log inline custom rich text icon matches and sprite swaps.
- `PortraitLogging`: Log speaker portrait lifecycle and resolution details.

---

## Optional Dependencies

KupoUI.PR does not hard-reference `Memoria.FFPR`, `Magicite`, or `FFPR_Fix`. At runtime, it checks loaded assemblies and logs whether those mods are present, enabling future integration paths without breaking standalone execution.
