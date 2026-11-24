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

**Step 10 (testing):** the full roster.

- **All 12 Sinners** (base LCB Identities): Yi Sang, Faust, Don Quixote, Ryoshu, Meursault, Hong Lu,
  Heathcliff, Ishmael, Rodion, Sinclair, Outis and Gregor, each with three skills, their base ZAYIN
  E.G.O (names and Sin costs as in Limbus), defense and passive. Numbers are fan-made.
- **Extra Identities:** Blade Lineage Salsu Yi Sang, Shi South Section 5 Heathcliff and R Corp.
  4th Pack Rabbit Heathcliff.
- **Second Boss: Erlking Heathcliff** (The Wild Hunt). Boss Rush now has two Bosses.
- **Arcade** picks five different Sinners per run, a random Identity of each, then the Bosses.
- Balance is checked with CPU-vs-CPU tests: in a 15-fighter round robin no fighter wins more
  than 85% or less than 15% of its matches.

**Step 9 (done):** Bosses.

- **Boss rules** (a `boss` block in the fighter file, editable in F2 under BOSS): **super armor**
  (hits during its wind-up and active frames hurt but do not interrupt; breaking its Stagger bar
  removes the armor), drawn bigger, and an **enraged phase 2** below half health (+damage, +clash
  power, ENRAGED banner). Bosses have more health, a bigger body, and are slower.
- **First Boss: Vergilius, the Red Gaze** (fan-made moves). The CPU boss beats a Normal CPU
  Sinner about 7 times in 8, in fights of about 25 seconds, so a careful player can win.
- **Boss Rush:** every Boss in a row, one round each. Arcade now ends with the Bosses.
- Built-in Sinners have more health (about 170-200), so rounds last longer.

**Step 8 (done):** playing alone.

- **CPU opponent** in three levels (Easy, Normal, Hard): walks to the range its skills reach,
  attacks, punishes whiffs and Staggers, defends on reaction (Hard reacts fast and often, Easy
  rarely) and uses E.G.O when it has the Sins. Hard beats Easy 32 of 32 times in the tests.
- **Arcade:** pick a fighter, then beat a shuffled row of the other Sinners (up to 5, Bosses at
  the end); the last two fights are one CPU level harder. Losing offers *Continue*; clearing it
  shows the Arcade Clear card with continues used.
- **Versus CPU:** one match, you pick both fighters.
- **Training options** (Esc in Training): dummy mode (stand, fight back, defend, CPU), CPU level,
  infinite health (refills after a second, no K.O.), free E.G.O, hitboxes. No round timer.
- **Options:** CPU level, rounds (1, best of 3, best of 5), round time (30 / 60 / 99 / no timer),
  fullscreen, window size. Saved in `%LOCALAPPDATA%\LimbusFight\settings.json`.
  **F11** or **Alt+Enter** switches fullscreen anywhere.

**Step 7 (done):** a real game around the fighting.

- **Title screen, main menu, character select, pause menu (Esc) and a results card.**
- **Versus:** two players on one keyboard, best of 3 (round pips under the timer).
  Player 2 uses the arrows, Numpad 1 / 2 / 3 (or `,` `.` `/`) for skills, Numpad 0 or Right Shift
  to defend and Numpad Enter (or `'`) for E.G.O.
- **Training:** the old sandbox, now picked from the menu: choose your fighter, then the dummy.
  F1, F2, F3, F4, F6 and R work here as before.
- **Built-in Sinners:** Yi Sang, Faust, Don Quixote and Ryoshu, each with their own numbers, three
  skills, an E.G.O (Crow's Eye View, Representation Emitter, La Sangre de Sancho, Forest for the
  Flames), defense and passive. They are fighter files like any other (edit them in F2; an edited
  one is never overwritten by game updates).
- **Sinner looks by themselves:** on start the game looks through your Limbus files for sprites
  named after each built-in Sinner (or their Identity number), builds an auto look from the best
  file and saves it in the fighter file. Progress shows on the title, menu and select screens.
  Fighters it cannot find keep a plain coloured box; F3 + Ctrl+A still makes a look by hand.

**Step 6 (done):** the first complete ID.

- **Skills 1-3 on J / K / L**, each its own move with frame data, hitbox, damage, type, Sin and
  statuses. **Coins** = hits: a 3-coin skill hits three times, a few frames apart.
- **Defense on S**, one per fighter: **Guard** (hold: a quarter of the damage, full Stagger
  damage, blockstun), **Evade** (press: backstep with invincible frames) or **Counter** (press: a
  parry window; a hit inside it is cancelled and the attacker reels).
- **E.G.O on I**: paid with Sin resources (training box: 4 Wrath), not while panicking, with a
  grade (ZAYIN ... ALEPH) and its own banner. The HUD shows how close it is.
- **Passive**, one per fighter: +clash power, +damage below half health, +Sanity per hit, a status
  on every hit, or less Stagger damage. **Charge** can now be spent by a move for extra damage.
- **Looks:** new clips for Skill 2, Skill 3, E.G.O, defense and evade, plus an effect per skill
  (a 3-coin skill replays its effect for each coin). Empty clips fall back sensibly (Skill 2 uses
  Skill 1's poses, evade uses jump). Auto look sorts S1 / S2 / S3, guard, evade and E.G.O names
  and their effects. **Ctrl+Q** in F3 adds the picked sprite to the clip selected in F4.
- **F2**: Q / E switch between Skill 1, 2, 3 and E.G.O; every move field, coins, E.G.O cost,
  Charge, plus the defense and passive. Older fighter files gain the new moves automatically.
- **F6** dummy: stand still, fight back (rotates its skills, uses E.G.O when it can), or defend.

**Step 5 (done):** core combat. A full round plays out against the dummy.

- **Health and K.O.:** rounds of 60 seconds; K.O. or time over (more health wins) ends a round, the
  next starts by itself, and wins are counted. R restarts the round.
- **Stagger:** every hit takes Stagger damage; an empty bar Staggers the fighter for 2 seconds
  (cannot act, takes x1.5 damage), then the bar refills.
- **Sanity (SP, -45 to +45):** landing hits and winning clashes raise it, taking hits and losing
  clashes lower it. It changes damage by up to +/-10% instead of coin flips. At -45 the fighter
  Panics for 3 seconds (less damage, no Sin gain), then returns to 0.
- **Clashes:** when both hitboxes are out and touch, the higher clash power (move + Sanity) wins:
  the loser's attack is cancelled and it is knocked back; equal power cancels both.
- **Damage types and resistances:** Slash / Pierce / Blunt multipliers per fighter, shown as
  Fatal / Weak / Normal / Endured / Ineffective on hit.
- **Sins:** landing a move fills its Sin's resource (seven pips in the HUD), for E.G.O in Step 6.
- **Statuses** (potency x count): Burn ticks every second, Bleed hurts when its bearer attacks,
  Tremor adds Stagger damage, Rupture adds damage, Sinking drains Sanity, Poise fills a crit meter
  (no randomness: a crit every time it reaches 100), Charge is stored for Step 6 moves.
- **F6:** the dummy stands still, or fights back (walks in and punches), so clashes, trades and
  being knocked out can be tested.
- **F2** edits HP, Stagger bar, resistances, and the punch's damage, Stagger damage, clash power,
  damage type, Sin and the statuses it gives or gains. The whole fight is deterministic.

**Step 4 (done):** fighters as data.

- **One file per fighter** in `%LOCALAPPDATA%\LimbusFight\fighters\<name>.json`: name, body boxes
  (hurtbox, pushbox), movement (walk speeds, jump, gravity, crouch / landing frames), moves with
  frame data (startup, active, recovery, hitstun, hitstop, pushback) and hitbox, and the look
  (clips of Limbus sprite names). Numbers and names only, never pictures: fighter files can be
  shared. `roster.json` remembers which fighter is P1 and which is the Dummy.
- **F2 fighter editor:** switch the fighter in each slot (`[` `]`), make a new one as a copy
  (Ctrl+N), rename it (Enter on its name), and change every number with the arrow keys. A preview
  at game size steps through the punch frame by frame with its pose, swing effect, hurtboxes,
  hitbox and pushbox, a startup / active / recovery timeline and the punch's frame advantage on hit.
  Changes are saved and live in the arena immediately.
- A fighter file with default numbers plays exactly like the old training box (checked frame for
  frame). Looks made in Step 3 move into the first two fighter files automatically.
- Ctrl+A in F3 names a still-unnamed fighter after the Sinner it detects (e.g. "Yi Sang").

**Step 3 (done):** sprites in the arena.

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
    ├── Program.cs                   window setup, --selftest / --scan-assets
    ├── GameApp.cs                   screens (title, menu, select, fight, tools), 60 FPS loop
    ├── Core/                        engine-independent services
    │   ├── SteamChecker.cs          Steam / Limbus Company detection + version gate constants
    │   ├── SteamCheckResult.cs      status object, StateFlags decoding
    │   ├── ValveKeyValues.cs        VDF / ACF parser
    │   ├── FixedStepClock.cs        exact 60 Hz simulation ticks
    │   ├── GameSettings.cs          options (settings.json)
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
    ├── Roster/                      fighters as data (no Raylib types)
    │   ├── FighterData.cs           fighter file format: body, movement, moves, look
    │   ├── FighterStore.cs          load / save fighter files + roster.json, first-run defaults
    │   ├── Presets.cs               the built-in Sinners (written to the fighters folder)
    │   ├── ArcadeLadder.cs          Arcade: the row of CPU opponents
    │   ├── AutoRoster.cs            finds each Sinner's sprites in the player's files, builds looks
    │   └── RosterManager.cs         which fighter is in each slot; switch, copy, rename, edit
    ├── Animation/                   looks: data files and clip choice (no Raylib types)
    │   ├── LookData.cs              look file format (clips of sprite references) + load/save
    │   ├── AutoLook.cs              sorts a file's sprites into clips by name (or shape)
    │   └── ClipPicker.cs            fighter state -> clip + frame (punch synced to its frames)
    ├── Gameplay/                    pure simulation (no Raylib types)
    │   ├── Box.cs  FrameInput.cs  AttackData.cs  FighterStats.cs  Fighter.cs
    │   ├── Combat.cs                Sins, damage types, statuses, combat constants
    │   ├── CpuController.cs         the computer opponent (Easy / Normal / Hard)
    │   └── TestArena.cs             rounds, hits, clashes, statuses, the fighting dummy
    ├── Input/KeyboardSampler.cs     keyboard -> per-tick input
    ├── Rendering/                   stage, fighters, debug boxes, window scaling, look textures
    ├── UI/                          status panel, update screen, debug HUD, asset browser, look + fighter editors, theme
    └── ThirdParty/CrnUnity/         Crunch texture decoder (MIT / zlib)
```

## Download the exe

Every push to `main` replaces the **Latest build** pre-release:

https://github.com/cng605568-art/limbus-fight/releases/download/latest-build/LimbusFight.exe

(also under **Releases** on the repo page). Builds go to a release rather than Actions
artifacts so they never use the account's 500 MB artifact storage quota.

## Controls

| Action | Player 1 | Player 2 |
| --- | --- | --- |
| walk | A / D | Left / Right |
| jump | W | Up |
| Skill 1 / 2 / 3 | J / K / L | Numpad 1 / 2 / 3 (or `,` `.` `/`) |
| defend (guard, evade or counter) | S | Numpad 0 or Right Shift |
| E.G.O | I | Numpad Enter (or `'`) |

Menus: W / S or the arrows move, J or Enter chooses, K or Esc goes back. Esc pauses a match.
F11 or Alt+Enter: fullscreen.

Training only:

| Key | Action |
| --- | --- |
| R | restart the round |
| F1 | hurtboxes (blue), hitboxes (red), pushboxes (yellow), FPS + frame counter |
| F2 | fighter editor (Tab P1 / Dummy, `[` `]` switch fighter, Ctrl+N new fighter, Enter on the name rename, Up/Down pick, Left/Right change, Shift bigger steps, Del reset value, Space play / pause, `,` `.` step frames) |
| F3 | asset browser (type to filter, Enter open, Tab switch list, Ctrl+S save the picked sprite or sheet as PNG, Ctrl+1..5 add it to P1's idle / walk / jump / punch / hit, Ctrl+6 / 7 to the swing / hit effect, Ctrl+A auto look from the open file, Ctrl+Z undo it, add Shift for the Dummy, Ctrl+O open exports, Ctrl+R report) |
| F4 | look editor (Tab P1 / Dummy, Up/Down clip, Left/Right frame, + / - hold, `,` `.` move frame, Del remove, PgUp/PgDn size, Home auto size, F flip, A anchor, Ctrl+arrows nudge) |
| F6 | dummy: stand still / fight back / defend / CPU |
| F5 | re-scan Steam, retry loading look pictures (and re-scan the asset folders when the browser is open) |
| Esc | training options and pause (dummy, CPU level, infinite health, free E.G.O, hitboxes, restart, ...) |

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
