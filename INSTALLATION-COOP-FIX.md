# Install Casualties: Together Co-op Fix 5.5.5-coopfix.3

Use the Windows full game, version 1.0.2, with the upstream 5.5.4 full-game mod or an earlier Co-op Fix release already installed. [Upstream 5.5.4 release](https://github.com/creaturefeaturelarry/casualties-together/releases/tag/v5.5.4)

1. Close the game. In Steam, open **Manage → Browse local files** for Casualties: Unknown.
2. Back up `BepInEx/plugins/Together/CasualtiesMP.dll` outside the game folder. Move `Multiupdater.dll` and the upstream updater patcher (`CT_AutoUpdateApplier.dll` or `autoupdater_patcher.dll`) outside the game folder if present. This keeps the updater from replacing the fork.
3. Extract the ZIP and copy its `BepInEx` folder into the game folder. Confirm replacement of `CasualtiesMP.dll` and keep `BepInEx/patchers/CoopFixUpdatePatcher.dll` in place.
4. Launch the game through Steam. Use the same fork build on the host and every joining player.

Open the **el sus** tab beside **Players** and **Rules** in the multiplayer menu. Knockout recovery speed defaults to 3× and can be set from 1× to 10×. Silent teammate alerts are off by default. The **Rules** tab's presets also save these settings.

The **el sus** tab also has the **Automatically update this fork** switch and a **Check for updates** button. Automatic updates are on by default. On launch, the mod checks this fork's GitHub releases for a newer Windows build for your game version. When the tab says an update is ready, restart the game normally to install it.

To roll back an automatic update, turn off automatic updates in **el sus** and close the game. In `BepInEx/plugins/Together`, copy `CasualtiesMP.dll.coopfix-backup` over `CasualtiesMP.dll`. If the game cannot start, first move `CoopFixUpdatePatcher.dll` outside the game folder, then restore the backup. To return to the upstream mod, remove the fork's updater patcher and restore the original DLLs from step 2.

The ZIP contains the replacement mod DLL and the updater patcher. Upstream code is credited in the fork and distributed under the included LGPL-2.1 license. [Fork source](https://github.com/KnowledgeMonkey/casualties-together/tree/v5.5.5-coopfix.3)
