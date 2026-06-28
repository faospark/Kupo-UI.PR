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
