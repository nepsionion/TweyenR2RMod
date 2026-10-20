# Art

Drop hand-made assets for the mod here. Claude picks them up from this folder.

| Folder | What goes in it |
|---|---|
| `Icons/Character` | Character portrait / select icon |
| `Icons/Skills` | Skill icons (one per skill) |
| `Icons/Skins` | Skin icons (one per colour variant) |
| `Other` | Anything else (UI art, references, notes) |

- PNG works best (transparency is kept). Square images, 128x128 or larger, for icons.
- Any file name is fine; descriptive names help (e.g. `merculight.png`).
- Folders named `temp` are ignored by the build (use them for reference images).
- Images in `Icons/` are packed into the mod's asset bundle on the next build. A file named like a placeholder
  (`texTweyenPortrait.png`, `texTweyenSkin00.png` ... `texTweyenSkin10.png`, `texTweyenSkillPlaceholder.png`)
  replaces that placeholder directly.

## Sounds

| Folder | What goes in it |
|---|---|
| `Sounds/Spawn` | Sound when Tweyen spawns (start of a stage) |
| `Sounds/Hurt` | Sound when she takes damage (one or more; several = picked at random) |
| `Sounds/Death` | Sound when she dies |
| `Sounds/CharacterSelect` | Sound when she is picked in the lobby |

- WAV works best (OGG/MP3 are fine too). Any file name.
- Nothing here is packaged yet: the sounds are added to the mod together, once these are in.
