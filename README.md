# Limbus Fight

A 2D fighting fangame of *Limbus Company*, written in pure C# (.NET 8) on top of
[Raylib-cs](https://github.com/raylib-cs/raylib-cs). No game engine.

This repository contains **no Limbus Company assets**. Later steps will read sprites and audio
directly from the player's own Steam install (App ID `1973530`), which is why the Steam
detection lives in its own module from day one.

> Unofficial fan project. Not affiliated with or endorsed by Project Moon.

## Step 1 status

- Project foundation (.NET 8, Raylib-cs 8.1.0 / raylib 6.0)
- GitHub Actions builds one self-contained `LimbusFight.exe` on every push to `main`
- Steam detection: registry + `libraryfolders.vdf` (any drive) + `appmanifest_1973530.acf`
- 60 FPS debug window: Steam status, two test fighters, F1 hitbox / FPS overlay, red update screen

## Layout

```
LimbusFight/
├── .github/workflows/build.yml      CI: single-file win-x64 exe, smoke test, artifact upload
├── LimbusFight.sln
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
    ├── Gameplay/                    pure simulation (no Raylib types)
    │   ├── Box.cs  FrameInput.cs  AttackData.cs  Fighter.cs  TestArena.cs
    ├── Input/KeyboardSampler.cs     keyboard -> per-tick input
    ├── Rendering/ArenaRenderer.cs   stage, fighters, debug boxes
    └── UI/                          status panel, update screen, debug HUD, theme
```

## Download the exe

Every push to `main` replaces the **Latest build** pre-release:

https://github.com/cng605568-art/limbus-fight/releases/download/latest-build/LimbusFight.exe

(also under **Releases** on the repo page). Builds go to a release rather than Actions
artifacts so they never use the account's 500 MB artifact storage quota.

## Controls (Step 1)

| Key | Action |
| --- | --- |
| A / D | walk |
| W | jump |
| J | punch |
| R | reset positions |
| F1 | hurtboxes (blue), hitboxes (red), pushboxes (yellow), FPS + frame counter |
| F5 | re-scan Steam |
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

- `LimbusFight.exe --selftest` prints the full Steam scan log and checks that the bundled
  raylib library loads. No window is opened.
- Portable / unusual Steam install: set `LIMBUSFIGHT_STEAM_PATH` to the folder that contains `steamapps`.
- Crashes write `LimbusFight_crash.log` next to the exe (or to `%TEMP%` if that folder is read-only).
