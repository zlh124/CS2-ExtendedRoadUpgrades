![GitHub all releases](https://img.shields.io/github/downloads/ST-Apps/CS2-ExtendedRoadUpgrades/total)
![GitHub release (latest by SemVer including pre-releases)](https://img.shields.io/github/downloads-pre/ST-Apps/CS2-ExtendedRoadUpgrades/latest/total)
﻿[![CC BY-NC-SA 4.0][cc-by-nc-sa-shield]][cc-by-nc-sa]

This work is licensed under a
[Creative Commons Attribution-NonCommercial-ShareAlike 4.0 International License][cc-by-nc-sa].

[![CC BY-NC-SA 4.0][cc-by-nc-sa-image]][cc-by-nc-sa]

[cc-by-nc-sa]: http://creativecommons.org/licenses/by-nc-sa/4.0/
[cc-by-nc-sa-image]: https://licensebuttons.net/l/by-nc-sa/4.0/88x31.png
[cc-by-nc-sa-shield]: https://img.shields.io/badge/License-CC%20BY--NC--SA%204.0-lightgrey.svg

# Cities: Skylines 2 - Extended Road Upgrades Continued

This mod enables both **quays**, **retaining walls** and **elevated** upgrade options in the Network Upgrade tool.

## License and attribution

This repository is a **modified version** of [Extended Road Upgrades](https://github.com/ST-Apps/CS2-ExtendedRoadUpgrades)
by ST-Apps, which is licensed under [CC BY-NC-SA 4.0][cc-by-nc-sa]. The original work is copyright (c) 2023-24
ST-Apps (S. Tenuta); see [NOTICE.txt](./NOTICE.txt) and [LICENSE.txt](./LICENSE.txt).

Because the license is *ShareAlike*, this continuation - including the published mod - is distributed under the same
license, [CC BY-NC-SA 4.0][cc-by-nc-sa]: attribution must be kept, it may not be used commercially, and modified or
redistributed versions must carry the same license. The changes made here are listed in [CHANGELOG.md](./CHANGELOG.md)
(ported to game 1.6, renamed, published as a separate PDX Mods entry). This continuation is not affiliated with,
sponsored or endorsed by the original author.

The published mod bundles Harmony (`0Harmony.dll`, MIT) - its notice is in
[THIRD-PARTY-NOTICES.txt](./THIRD-PARTY-NOTICES.txt).

> [!IMPORTANT]
> **This is not the original mod.** It is an independent continuation of
> [Extended Road Upgrades](https://github.com/ST-Apps/CS2-ExtendedRoadUpgrades) by ST-Apps, which was last updated for
> game version 1.4 and no longer loads with game versions 1.5 / 1.6 (its compiled code calls game methods that have
> been removed, so it fails at startup). This build was ported to the current game version and is published as a
> **separate mod entry**.
>
> **The two versions cannot be enabled at the same time.** Both register the same upgrade prefabs (Quay, Retaining
> Wall, Elevated, Tunnel) and patch the same game methods, so enabling both produces duplicated upgrade modes and
> conflicting road data. Unsubscribe from / disable the original mod before enabling this one, then restart the game.
> If the mod detects prefabs created by the other version it refuses to touch them and logs an error in
> `Logs\Extended Road Upgrades Continued.log`.

> [!WARNING]  
> This mod might mess up your save game. If this happens and some of your roads are acting weird you can bulldoze and recreate them to fix any issue.

[CHANGELOG](./CHANGELOG.md)

# Description

This mod enables some additional upgrade modes for your roads, giving you full control on the type of roads you can build without needing to precisely terraform beforehand.

These upgrades are available in the in-game upgrade mode for roads and can be used exactly like any other upgrade mode (e.g. Grass or Wide Sidewalk).

The mod currently supports the following modes:
- Quay
- Retaining Wall
- Elevated
- Tunnel

These upgrades can be applied to:
- Roads
- Pedestrian Paths
- Public Transport Roads
- Train Tracks
- Tram Tracks
- Subway Tracks

# Setup

> [!TIP]
> The mod is distributed through PDX Mods only, BepInEx is not a requirement anymore.
>
> Subscribe to [Extended Road Upgrades Continued](https://mods.paradoxplaza.com/mods/164028/Windows) (mod ID `164028`)
> and remember to **disable the original Extended Road Upgrades mod** if you still have it enabled.

- [Cities: Skylines 2](https://store.steampowered.com/app/949230/Cities_Skylines_II/)

# Building

The mod targets the game's own assemblies, so a local Cities: Skylines II installation is required.

```powershell
.\build.ps1              # builds and deploys to the local Mods folder
.\build.ps1 -NoDeploy    # builds only (output goes to .tmp\deploy)
```

The script locates the game (Steam libraries, `-CsiiGameDir` or `CSII_GAMEPATH`) and feeds the paths the
official modding toolchain (`Cities2_Data\Content\Game\.ModdingToolchain`) expects, so it also works on machines
where the toolchain's user-scoped environment variables (`CSII_TOOLPATH`, `CSII_MANAGEDPATH`, ...) were never set.
When those variables point at a complete toolchain (Unity editor + generated Unity Mods project, both installed by
the in-game toolchain setup), the build also runs the official Mod Post Processor and lets the toolchain deploy.

Everything the toolchain needs at build time is either shipped with the game or with this repository
(`libs\0Harmony.dll`). Two parts of the full toolchain are optional:

| Component | Needed for | When missing |
| --- | --- | --- |
| .NET Framework 4.8 targeting pack | building against the .NET Framework reference assemblies | the build compiles against the game's own assemblies instead (handled by `build.ps1`) |
| Unity editor + generated Unity Mods project | running the Mod Post Processor (Burst code extraction, platform libraries and publishing) | the mod is still built and deployed; only Burst post-processing is skipped, which this mod does not use |

# Publishing to PDX Mods

Releases go through the Paradox Mod Publisher that ships with the toolchain. The version, description and
changelog are taken from the project files, so a release is:

1. Bump `<Version>` in `ExtendedRoadUpgrades.csproj`.
2. Write the changelog in `Properties/LatestChangelog.md` (and add the same entry to `CHANGELOG.md`).
3. Make sure `Properties/PublishConfiguration.xml` points at the right `<ModId>` — `76971` is the existing PDX Mods
   entry, so subscribers get the update instead of a new mod.
4. Run the release:

```powershell
.\build.ps1 -Publish Update      # update the existing mod (ModId 76971)
.\build.ps1 -Publish Publish     # first-time publish of a new mod entry
.\build.ps1 -Publish NewVersion  # create a new version entry for the configured mod
```

`build.ps1` rebuilds and re-deploys first (so the Mod Post Processor regenerates the platform libraries), then invokes
`ModPublisher` with `Properties/PublishConfiguration.xml` and the deploy folder as content.

Authentication uses the session the game already stored. On a machine where that is not available, create
`Properties/pdx_account.txt` with the PDX account email on the first line and the password on the second; the build
passes it to the publisher with `--loginData`. The file is git-ignored.

The `SetDescription` target in `ExtendedRoadUpgrades.csproj` registers `Properties/LongDescription.md`,
`Properties/LatestChangelog.md` and `<Version>` into `Properties/PublishConfiguration.xml` on every build, so those
files are the single source of truth.

# Usage

You will find new icons in the Road Upgrade menu, just after the _Grass_ one.

Select one of them and upgrade your roads as you would with the other available upgrades.

# Known Issues

> Please see the official [issue tracker](https://github.com/ST-Apps/CS2-ExtendedRoadUpgrades/issues).

# Thanks

Many thanks to the people of [Cities 2 Modding](https://discord.gg/DZaSSnRG) and [Cities: Skylines Modding](https://discord.gg/ey6kT5kf) Discord
communities for their help and pointers that helped me developing this mod.

Special thanks to [Chamëleon TBN](https://github.com/chameleon-tbn) for his beautiful icons and to [Captain of Coit](https://github.com/Captain-Of-Coit) for his mod template.

## Localization

- **zh-HANS** provided by [MaxHasBeenUsed](https://github.com/MaxHasBeenUsed)
- **zh-HANT** provided by [MaxHasBeenUsed](https://github.com/MaxHasBeenUsed)
