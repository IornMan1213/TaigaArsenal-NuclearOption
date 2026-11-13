# Building Taiga Arsenal

## Layout

| Folder | What it holds |
|---|---|
| `unity/TaigaTools/` | `TaigaArsenal.cs`, the Unity editor script that builds every store and the add-weapon ops (the supported aircraft are its `Jets` table) |
| `tools/` | `arsenal_gen.py` (the store models), `build_taiga.ps1` (the headless build and release zip) |
| `mod/doorman/` | the mod manager card and preview image that go in the release zip |
| `nomnom/` | the listing for the [NOMNOM](https://github.com/KopterBuzz/NOMNOM) mod registry (a draft until it is submitted) |
| `tests/` | in-game firing tests (`tests/ingame/`) and `run_all.sh` |
| `docs/` | the weapon list, this page and the README's pictures |

## Requirements

- A [Blueprinter editor](https://github.com/nikkorap/NOBlueprinter-Editor) project set up against your copy of the game.
- Unity 2022.3.62f2 (the Blueprinter editor's version), with Unity Hub running and signed in.
- PowerShell 7 (`pwsh`, on Windows or Linux) and Python 3.

## Build

```bash
pwsh tools/build_taiga.ps1 -Project <Blueprinter-Editor folder>
```

The script copies the editor script into the project, generates the models, runs a compile pass and then the build pass. Output
lands in `<project>/TaigaBuild/`:

- `Taiga Arsenal_<version>.nobp`, the mod;
- `TaigaArsenal_<version>.zip`, the release archive: the `.nobp`, its mod manager card and preview, `CREDITS.md`, `LICENSE` and `README.txt`.

For a test, copy the `.nobp` into `BepInEx/plugins/TaigaArsenal/`.

## Version

The version is `TaigaArsenal.Version` in `unity/TaigaTools/TaigaArsenal.cs`. Bump it, add a `CHANGELOG.md` section, build, and
attach the zip to a GitHub release named `v<version>`.

## Adding an aircraft

Add an entry to `Jets` in `TaigaArsenal.cs`: the aircraft's JSON key, a display name, and for each station role (cannon,
centreline, inner, middle, outer) the aircraft's hardpoint set indices. Leave a role empty (`new int[0]`) to offer nothing for it,
and list mount key prefixes in `Skip` for stores that jet should not carry. If the aircraft mod is not installed, Blueprinter logs
one "aircraft not found" line per op and carries on.

## In-game tests

The tests run through the NODevDriver test driver, which lives in the MiG-29 Pack repository (not public; expected beside this one, or set `MIG29PACK`).
Each run starts and quits the game, so only run them when nobody is playing:

```bash
tests/run_all.sh            # every run (A to N on the MiG-29, then the Su-30VXN)
tests/run_all.sh c su30     # some of them
```
