# NOMNOM

`TaigaArsenal.json` is the listing for [NOMNOM](https://github.com/KopterBuzz/NOMNOM), the mod registry used by NOMM
(`modManifests/TaigaArsenal.json` there). It marks the mod as work in progress: "(WIP)" in the name, the `work in progress` tag,
and pre-release artifacts (GitHub releases are published as pre-releases while the mod is WIP).

NOMNOM picks up new GitHub releases by itself and takes each release's first asset, `TaigaArsenal_<version>.zip`. When the mod
leaves WIP, publish full releases and update the name and tags in the NOMNOM listing with a pull request there.
