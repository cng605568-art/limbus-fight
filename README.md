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

**Step 3 (testing):** sprites in the arena.

- **Looks:** each fighter (P1 and the Dummy) has a look made of five clips: idle, walk, jump,
  punch and hit. In the F3 browser, pick a sprite and press **Ctrl+1..5** to add it to a P1 clip
  (hold **Shift** for the Dummy). The fighter is then drawn with those pictures instead of a box;
  hitboxes (F1) stay exactly where they were.
- **Auto look (Ctrl+A in F3):** builds all five clips from the open file in one key press.
  Sprite names are split into words and matched against keyword lists (idle / default / stand,
  walk / move / run, jump / air, attack / skill / S1, damage / hit / hurt; evade fills in for jump);
  numbered names (`attack_1`, `attack_2`...) become frames in order; effects and UI pictures
  (fx, glow, portrait, icon...) are skipped. If names say nothing, shapes decide: the most upright
  pose is idle and the widest poses are the punch. Each frame keeps a note saying why it was
  picked (shown in F4); Ctrl+Z undoes the auto look. Hold Shift for the Dummy.
- **Effects:** two more clips per look. *Swing fx* (Ctrl+6) is drawn on the punch's hitbox from
  its first active frame, so the slash appears exactly with the hitbox; *hit fx* (Ctrl+7) plays
  where a hit lands, through hitstop, replacing the yellow spark. Auto look fills them from effect
  sprites (slash / trail for the swing, hit / spark / impact for the hit); magic circles, portraits
  and other UI pictures are left out. Effects are mirrored with the fighter and drawn at the look's size.
- **Timing follows the moves:** punch frames are synced to the attack (1st = wind-up during
  startup, 2nd = the hit during active frames, the rest share recovery); jump frames follow the
  jump (crouch, rise, fall); idle, walk and hit play by each frame's hold time.
- **F4 look editor:** frame thumbnails per clip, hold times, re-order / remove frames, size,
  flip, anchor (bottom edge or Unity pivot) and nudge, with a game-sized preview against the
  hurtbox. Saved immediately to `%LOCALAPPDATA%\LimbusFight\looks\p1.json` / `dummy.json`.
- Look files hold **names only** (bundle folder, sprite name, sheet name, timing), never pixels.
  Pictures are cut from the player's own files at start-up; if a game update renames a bundle,
  the sprite is found again through the name index.

**Step 2 (done):** asset discovery.

- Finds both places Limbus Company keeps files: `LimbusCompany_Data` in the Steam install, and the
  bundles the game downloads to `%USERPROFILE%\AppData\LocalLow\Unity\ProjectMoon_LimbusCompany`.
- **F3 asset browser:** lists every Unity bundle, opens it, previews any texture inside
  (DXT/BC, ETC, ASTC, Crunch and raw formats) and saves it as PNG to
  `%LOCALAPPDATA%\LimbusFight\exports` on request (Ctrl+S).
- **Sprites:** bundles that hold sprite atlases (`sactx-…` sheets) list every sprite on them.
  Picking one cuts it out of its sheet: it follows the SpriteAtlas entry, undoes packing
  rotation/flips, and draws the sprite's own mesh (or masks it with the mesh outline for tight
  packing). The preview marks the sprite's pivot, which is where the game will stand it.
- **Search by name:** Limbus Company's bundles have hash names, so the first run reads the names
  of every texture, sprite and sound inside them. Typing in the browser then finds bundles by
  those names. Names and file headers are remembered in `%LOCALAPPDATA%\LimbusFight\cache`, so
  later runs only re-read files the game has changed.
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
    │   ├── AssetCache.cs            remembers headers + names between runs
    │   ├── AssetIndexer.cs          reads texture / sprite / sound names for search
    │   ├── UnityAssetReader.cs      opens bundles, lists textures + sprites, follows atlases (AssetsTools.NET)
    │   ├── SpriteCutter.cs          cuts one sprite out of its sheet (mesh UVs, rotation, tight mask)
    │   ├── LookLoader.cs            finds a look's sprites in the player's files and cuts them
    │   ├── TextureDecoding.cs       Unity texture formats -> RGBA
    │   ├── AssetReport.cs           LimbusFight_asset_report.txt
    │   └── AssetScanCli.cs          --scan-assets
    ├── Animation/                   looks: data files and clip choice (no Raylib types)
    │   ├── LookData.cs              look file format (clips of sprite references) + load/save
    │   ├── AutoLook.cs              sorts a file's sprites into clips by name (or shape)
    │   └── ClipPicker.cs            fighter state -> clip + frame (punch synced to its frames)
    ├── Gameplay/                    pure simulation (no Raylib types)
    │   ├── Box.cs  FrameInput.cs  AttackData.cs  Fighter.cs  TestArena.cs
    ├── Input/KeyboardSampler.cs     keyboard -> per-tick input
    ├── Rendering/                   stage, fighters, debug boxes, window scaling, look textures
    ├── UI/                          status panel, update screen, debug HUD, asset browser, look editor, theme
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
| F3 | asset browser (type to filter, Enter open, Tab switch list, Ctrl+S save the picked sprite or sheet as PNG, Ctrl+1..5 add it to P1's idle / walk / jump / punch / hit, Ctrl+6 / 7 to the swing / hit effect, Ctrl+A auto look from the open file, Ctrl+Z undo it, add Shift for the Dummy, Ctrl+O open exports, Ctrl+R report) |
| F4 | look editor (Tab P1 / Dummy, Up/Down clip, Left/Right frame, + / - hold, `,` `.` move frame, Del remove, PgUp/PgDn size, Home auto size, F flip, A anchor, Ctrl+arrows nudge) |
| F5 | re-scan Steam, retry loading look pictures (and re-scan the asset folders when the browser is open) |
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
