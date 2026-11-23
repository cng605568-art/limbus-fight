# Limbus Fight

A 2D fighting fangame of *Limbus Company*, written in pure C# (.NET 8) on top of
[Raylib-cs](https://github.com/raylib-cs/raylib-cs). No game engine.

This repository contains **no Limbus Company assets**. The game reads sprites and audio
directly from the player's own Steam install (App ID `1973530`), read-only, on the player's PC.

> Unofficial fan project. Not affiliated with or endorsed by Project Moon.

## Status

**Step 1 (done):** project foundation, GitHub Actions exe builds, Steam detection
(registry + `libraryfolders.vdf` on any drive + `appmanifest_1973530.acf`), 60 FPS debug window with
two test fighters, F1 hitbox / FPS overlay and the red update screen.

**Step 2 (testing):** asset discovery.

- Finds both places Limbus Company keeps files: `LimbusCompany_Data` in the Steam install, and the
  bundles the game downloads to `%USERPROFILE%\AppData\LocalLow\Unity\ProjectMoon_LimbusCompany`.
- **F3 asset browser:** lists every Unity bundle, opens it, previews any texture inside
  (DXT/BC, ETC, ASTC, Crunch and raw formats) and saves it as PNG to
  `%LOCALAPPDATA%\LimbusFight\exports` on request (Ctrl+S).
- **Asset report:** Ctrl+R in the browser (or `LimbusFight.exe --scan-assets`) writes
  `LimbusFight_asset_report.txt`: file names, sizes and formats only, user name redacted.
- Refuses to read anything while Limbus Company is running, and never writes to the game's folders.

## Layout

```
LimbusFight/
├── .github/workflows/build.yml      CI: single-file win-x64 exe, smoke tests, latest-build release
├── LimbusFight.sln
├── THIRD-PARTY-NOTICES.md           licenses of bundled libraries
└── src/LimbusFight/
    ├── LimbusFight.csproj
    ├── Program.cs                   window, 60 FPS loop, scene switch
    ├── Core/                        engine-independent services
    │   ├── SteamChecker.cs          Steam / Limbus Company detection + version gate constants
    │   ├── SteamCheckResult.cs      status object, StateFlags decoding
    │   ├── ValveKeyValues.cs        VDF / ACF parser
    │   ├── FixedStepClock.cs        exact 60 Hz simulation ticks
    │   ├── AppInfo.cs               version string
    │   └── Diagnostics.cs           crash log + --selftest
    ├── Assets/                      read-only access to the player's Limbus Company files
    │   ├── LimbusAssetRoots.cs      where the files are, is the game running
    │   ├── AssetScanner.cs          inventory by file header (bundle, audio bank, video...)
    │   ├── UnityAssetReader.cs      opens bundles, lists objects, reads textures (AssetsTools.NET)
    │   ├── TextureDecoding.cs       Unity texture formats -> RGBA
    │   ├── AssetReport.cs           LimbusFight_asset_report.txt
    │   └── AssetScanCli.cs          --scan-assets
    ├── Gameplay/                    pure simulation (no Raylib types)
    │   ├── Box.cs  FrameInput.cs  AttackData.cs  Fighter.cs  TestArena.cs
    ├── Input/KeyboardSampler.cs     keyboard -> per-tick input
    ├── Rendering/                   stage, fighters, debug boxes, window scaling
    ├── UI/                          status panel, update screen, debug HUD, asset browser, theme
    └── ThirdParty/CrnUnity/         Crunch texture decoder (MIT / zlib)
```

## Download the exe

Every push to `main` replaces the **Latest build** pre-release:

https://github.com/cng605568-art/limbus-fight/releases/download/latest-build/LimbusFight.exe

(also under **Releases** on the repo page). Builds go to a release rather than Actions
artifacts so they never use the account's 500 MB artifact storage quota.

## Controls

| Key | Action |
| --- | --- |
| A / D | walk |
| W | jump |
| J | punch |
| R | reset positions |
| F1 | hurtboxes (blue), hitboxes (red), pushboxes (yellow), FPS + frame counter |
| F3 | asset browser (type to filter, Enter open, Tab switch list, Ctrl+S save PNG, Ctrl+O open exports, Ctrl+R report) |
| F5 | re-scan Steam (and the asset folders when the browser is open) |
| ESC | quit |

## Version gate

Both switches live at the top of `src/LimbusFight/Core/SteamChecker.cs`:

- `TARGET_BUILD_ID`: the Limbus Company `buildid` the asset pipeline was verified against.
  `0` means "not set" (only `StateFlags == 4` is checked).
- `BYPASS_VERSION_CHECK`: `true` for early testing (status is shown, never blocks).
  Set it to `false` to show the red warning screen when the game is missing, updating or outdated.

## Local build

```
dotnet run --project src/LimbusFight
dotnet publish src/LimbusFight/LimbusFight.csproj -c Release -r win-x64 -o publish
```

## Diagnostics

- `LimbusFight.exe --selftest` prints the full Steam scan log and checks that raylib, the bundle
  reader and the texture decoders all load. No window is opened.
- `LimbusFight.exe --scan-assets` writes the asset report without opening a window.
- Portable / unusual Steam install: set `LIMBUSFIGHT_STEAM_PATH` to the folder that contains `steamapps`.
- Moved the game's download folder: set `LIMBUSFIGHT_ASSET_CACHE` to the moved `ProjectMoon_LimbusCompany` folder.
- Crashes write `LimbusFight_crash.log` next to the exe (or to `%TEMP%` if that folder is read-only).
