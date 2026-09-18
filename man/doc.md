# DOC

A kneeboard: cycle through your own reference images — airport diagrams, checklists, whatever you
keep on hand — without alt-tabbing out of the game.

## Adding images

Drop `.png`, `.jpg`, or `.jpeg` files into the mod's own kneeboard folder
(`BepInEx/plugins/NOXMFD/kneeboard/`, created automatically the first time DOC loads). The index
lists whatever's in there, in filesystem order — nothing to configure in-game.

## Index

DOC opens on an index of every image file in the folder. Click a file name to open it. An empty
folder shows **NO FILES** instead of a list.

## Viewing an image

- **INDX** — back to the index.
- **NEXT / PREV** — cycle to the next/previous file in the folder, wrapping at either end.

The image scales to fit the page width; a taller diagram extends the page rather than cropping —
scroll to see the rest of it.

The folder is re-read every time you open the index or press NEXT/PREV, so a file you add or remove
while playing shows up (or disappears) immediately, with no need to reopen the page.
