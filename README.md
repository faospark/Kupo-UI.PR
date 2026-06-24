# DarkerUI.PR

A BepInEx IL2CPP plugin scaffold for Final Fantasy Pixel Remaster (FF1-FF6).

This plugin includes runtime patches for UI cleanup and custom texture replacement across Final Fantasy Pixel Remaster (FF1-FF6).

## Features

- BepInEx IL2CPP plugin structure (`BasePlugin`)
- Harmony runtime patching (`PatchAll`)
- Soft dependency detection for:
  - `Memoria.FFPR`
  - `Magicite`
- Cross-game-safe first patch using Unity API (`UnityEngine.Cursor`)
- Layered custom texture system with shared and per-game folders

## Project Layout

- `DarkerUI.PR.csproj` - .NET Framework 4.7.2 class library project
- `src/DarkerUIPRPlugin.cs` - plugin entry point
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
dotnet build .\DarkerUI.PR.csproj -c Release -p:BepInExDir="D:\Games\FINAL FANTASY II PR\BepInEx"
```

## Install

Copy output DLL from:

- `bin/Release/net472/DarkerUI.PR.dll`

to:

- `BepInEx/plugins/DarkerUI.PR/`

## Config

Generated on first run in `BepInEx/config`:

- `General.DisableMouseCursor` (default: `true`)
- `Textures.EnableCustomTextures` (default: `true`)
- `Textures.TextureRootFolder` (default: `DarkerUI.PR\Textures`)
- `Textures.GameTagOverride` (default: empty, auto-detect)
- `Textures.LogTextureResolution` (default: `false`)
- `Textures.EnableTextureHotReload` (default: `true`)
- `Textures.TextureHotReloadDebounceMs` (default: `350`)
- `Textures.EnableDDSTextures` (default: `true`)
- `Textures.EnableTextureLogger` (default: `true`)
- `Textures.LogTextureDiscoveries` (default: `true`)
- `Textures.LogTextureResolutions` (default: `true`)
- `Textures.LogTextureMisses` (default: `false`)

Set to `false` to disable this initial patch while keeping the plugin active.

## Custom Texture Folder Layout

Default root:

- `BepInEx/plugins/DarkerUI.PR/Textures/`

Supported layers:

- `Shared/` (all games)
- `FF1/` .. `FF6/` (game-specific)
- `00-Mods/Shared/` (override shared)
- `00-Mods/FF1/` .. `00-Mods/FF6/` (highest priority per-game override)

Lookup priority (highest to lowest):

1. `00-Mods/<CurrentGame>`
2. `00-Mods/Shared`
3. `<CurrentGame>`
4. `Shared`

Use file names without extension to match in-game texture/sprite names (for example `window_frame.png` to replace `window_frame`).

### Path-based overrides (recommended for collisions)

Many FFPR assets reuse the same file names in different bundles. To avoid collisions, DarkerUI.PR also supports path-based resolution for files placed under a `GameAssets` folder.

How it works:

- If a replacement file path contains a `GameAssets/...` segment, the resolver indexes it by full relative path (without extension), not only by file name.
- At runtime, when the game loads an address like `Assets/GameAssets/...`, DarkerUI.PR can resolve to the exact matching replacement path first.
- Name-only matching still works as a fallback for existing setups.

Example (FF2 portrait):

- In-game address: `Assets/GameAssets/Serial/Res/Chara/Face/FA_FF2_P001/Default_00.png`
- Replacement file location:
  - `BepInEx/plugins/DarkerUI.PR/Textures/FF2/GameAssets/Serial/Res/Chara/Face/FA_FF2_P001/Default_00.png`

This allows `Default_00.png` files from different portrait folders/bundles to be replaced independently.

### Optional sidecar metadata

You can add a JSON file next to a replacement texture with the same base name to override logical size and filtering.

Example:

- `Default_00.png`
- `Default_00.json`

Example JSON:

```json
{
  "width": 112,
  "height": 144,
  "filterMode": "Point"
}
```

Supported fields:

- `width`: logical source width used when calculating replacement sprite scale
- `height`: logical source height used when calculating replacement sprite scale
- `filterMode`: Unity-style filter override, one of `Point`, `Bilinear`, or `Trilinear`
- `pointFilter`: legacy boolean shorthand, `true` = `Point`, `false` = `Bilinear`

If both `filterMode` and `pointFilter` are present, `filterMode` takes priority.

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
