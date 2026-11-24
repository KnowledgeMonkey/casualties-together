# Install Casualties: Together Co-op Fix 5.5.5-coopfix.1

Use the Windows full game, version 1.0.2, with the upstream 5.5.4 full-game mod and its dependencies already installed. [Upstream 5.5.4 release](https://github.com/creaturefeaturelarry/casualties-together/releases/tag/v5.5.4)

1. Close the game. In Steam, open **Manage → Browse local files** for Casualties: Unknown.
2. Back up `BepInEx/plugins/Together/CasualtiesMP.dll` outside the game folder. Move `Multiupdater.dll` and the upstream updater patcher (`CT_AutoUpdateApplier.dll` or `autoupdater_patcher.dll`) outside the game folder if present. This keeps the updater from replacing the fork.
3. Extract the ZIP and copy its `BepInEx` folder into the game folder. Confirm replacement of `CasualtiesMP.dll`.
4. Launch the game through Steam. Use this same build on the host and every joining player. The startup log at `BepInEx/LogOutput.log` shows `5.5.5-coopfix.1` when the fork loads.

To revert, close the game and put the backed-up DLLs back in their original locations.

The ZIP contains the replacement mod DLL. Upstream code is credited in the fork and distributed under the included LGPL-2.1 license. [Fork source](https://github.com/KnowledgeMonkey/casualties-together/tree/v5.5.5-coopfix.1)
