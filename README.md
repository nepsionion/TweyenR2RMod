# TweyenR2RMod

A Risk of Rain 2 survivor mod (Tweyen from Granblue Fantasy), built on the [Henry survivor template](https://github.com/ArcPh1r3/HenryTutorial) ([template wiki](https://github.com/ArcPh1r3/HenryTutorial/wiki)).

Download the mod from Thunderstore. Suggestions and bug reports are welcome in this repository's [Issues](https://github.com/nespionion/TweyenR2RMod/issues).

This repository holds the source only: the files extracted from Granblue Fantasy Relink (`GBFRExtract/`, `TweyenUnityProject/TweyenSource/`, the generated `TweyenUnityProject/Assets/TweyenAssets/`), the sounds, the built asset bundle and sound bank, third-party tools (`GBFRDataTools/`, `Tools/vgmstream/`) and other mods' DLLs (`TweyenR2RMod/libs/`: Risk of Options, StandaloneAncientScepter) are not included.

## Layout

| Folder | What it is |
|---|---|
| `TweyenR2RMod/` | C# plugin (BepInEx). `Modules/` is the reusable template framework, `Characters/Survivors/` holds survivor code. |
| `TweyenUnityProject/` | Unity project used to build the `tweyenbundle` AssetBundle (models, animations, materials, icons). Open with Unity **2021.3.45f2** (the version Risk of Rain 2 uses). |
| `Thunderstore/` | The release package: `manifest.json`, `README.md`, `CHANGELOG.md`, `icon.png`, and `plugins/TweyenR2RMod/` laid out exactly like the installed mod (DLL, `AssetBundles/`, `Soundbanks/`). |
| `GBFRDataTools/` | Tool for extracting Granblue Fantasy Relink assets (not included; `Tools/GBFRDataTools-minfo-to-gltf.patch` is the change applied to it). |

## Building

`dotnet build` (or build in Visual Studio / Rider). After each build the DLL, plus `TweyenUnityProject/AssetBundles/tweyenbundle` if it exists, is copied into `Thunderstore/plugins/TweyenR2RMod/`, and that folder is mirrored into the Thunderstore Mod Manager **Testing** profile.

## Multiplayer

The plugin declares `NetworkCompatibility(EveryoneMustHaveMod, EveryoneNeedSameModVersion)`: every player needs the same version installed.

## Rebuilding the character assets

1. `Tools/export_unity.py GBFRExtract TweyenUnityProject/TweyenSource` converts the extracted Granblue Fantasy Relink data (see `Tools/`) into the format the Unity builder reads.
2. `Unity.exe -batchmode -nographics -quit -projectPath TweyenUnityProject -executeMethod TweyenBuilder.BuildAll` regenerates `Assets/TweyenAssets/TweyenBundle` and builds `AssetBundles/tweyenbundle` (also available in the editor under **Tweyen > Build All**). **Tweyen > Validate** renders test poses to `TweyenUnityProject/ValidationRenders`.
3. `dotnet build` in `TweyenR2RMod/` copies the bundle and DLL into the Thunderstore folder and the Testing profile.
