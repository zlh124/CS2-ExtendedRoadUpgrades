# 1.0.1

- [ADD] License and third-party notices (LICENSE.txt, THIRD-PARTY-NOTICES.txt) are now shipped inside the mod and the mod page documents the CC BY-NC-SA 4.0 attribution and the bundled Harmony MIT notice

# 1.0.0

First release of the independent continuation of Extended Road Upgrades, ported to game version 1.6. Versioning restarts at 1.0.0 because this is published as a separate mod entry.

- [NEW] This is a separate mod from the original Extended Road Upgrades: the two cannot be enabled at the same time (disable the original first)
- [FIX] Mod not loading anymore with game versions 1.5 / 1.6 (the old build called game methods that no longer exist)
- [FIX] Upgrade prefabs are now copied with the game's own clone routine: no more "referenced from another prefab" errors and no more corruption of the vanilla upgrade
- [FIX] Upgrades are installed even if the game prefabs are not ready yet at load time
- [ADD] A clear error is logged when another version of the mod already registered the upgrade prefabs
