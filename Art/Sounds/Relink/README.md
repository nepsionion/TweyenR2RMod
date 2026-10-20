# Tweyen's sounds

The bank is built by `Tools/soundbank/build_soundbank.py` (no Wwise authoring tool) into
`TweyenR2RMod/Soundbanks/TweyenSounds.sound`; the list of what goes in is `EVENTS` in that script.
The `.wav` files in this folder are only samples of the picks (the bank takes every Relink variant from the game files).

| Event | Source | Plays |
|---|---|---|
| Shot | Relink 036 + 037 (random) | Arrow fired: Stellar Arrow, Wheel of Death, Valiant Weave, Two-Crown's Strife (once per volley), Multilock Hail |
| Mark | Relink 041 (4 variants) | Each new Asteroid Jaeger mark (only the aiming player hears it) |
| JaegerCharge | Relink 075 | Asteroid Jaeger aim start; stops when the aim ends |
| ClincherDraw / ClincherShot | Relink `pl2300_ab_03_charge` / `_shoot` (2 variants each) | Clincher |
| MoveLoop | Relink 079 + 084 (exactly those clips), together, looping | While she moves on the ground (replaces footsteps: she floats) |
| Jump / Land | Relink `pl2300_mov_jump` / `pl2300_foot_land` (default ground) | Movement (no sprint-start sound) |
| HurtS / HurtM / HurtL | Relink Japanese voice `DMG_default_s/m/l` | When hurt (by the size of the hit) |
| Death | Relink Japanese voice `DMG_down` | Death |
| Spawn / Select / TwoCrown | `Art/Sounds/Spawn`, `CharacterSelect`, `TwoCrown` | Spawn, character select, Two-Crown's Strife |

Bow draw for every skill but Clincher: the Huntress's `Play_huntress_m1_ready` (vanilla).

Volume: the arrow shot at -16 dB (`QUIET`), everything else 8 dB louder at -8 dB (`LEVEL`; Mark -16, move loop 079 -10 / 084 -6). Wwise property 0 = volume, 5 = make-up gain.
