# DOC

A kneeboard: cycle through your own reference images — airport diagrams, checklists, whatever you
keep on hand — without alt-tabbing out of the game.

## Adding images

Drop `.png`, `.jpg`, or `.jpeg` files into the mod's own kneeboard folder
(`BepInEx/plugins/NOXMFD/kneeboard/`, created automatically as soon as the plugin loads). The index
lists whatever's in there, in filesystem order — nothing to configure in-game.

Reached from [MAIN](main.md) via **MD**, alongside [AKF](akf.md)/[MIS](mis.md)/[OBJ](obj.md)/
[BDF](bdf.md)/[PAL](bdf.md).

## Index

DOC opens on an index of every image file in the folder. Click a file name to open it. An empty
folder shows **NO FILES** instead of a list. Its only nav keys here are **MAIN** and **MD** (back to
the mission-data hub) — INDX/NEXT/PREV only appear once an image is actually open.

## Viewing an image

- **INDX** — back to the index.
- **NEXT / PREV** — cycle to the next/previous file in the folder, wrapping at either end.

The image scales to fit the page width; a taller diagram extends the page rather than cropping —
scroll to see the rest of it.

The folder is re-read every time you open the index or press NEXT/PREV, so a file you add or remove
while playing shows up (or disappears) immediately, with no need to reopen the page.
