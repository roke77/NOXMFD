# UI

Display settings, reached from MAIN's CFG key alongside [HUD](hud.md), [KEY](keybinds.md) and
[LYT](layouts.md).

## COLOURS

Recolours the whole MFD with saved colour themes. The active theme applies to every connected
browser and device, every page, and extension pages too; changes show immediately, with no reload.
The CLASSIC bezel and the F-35 frame keep their own colours.

### THEMES

- **DEFAULT** is the stock colour set. It is read-only: changing a colour while it is active asks
  for a name and saves a new theme with that change.
- Tap a theme card to switch to it.
- **NEW** saves a copy of the active theme under a new name.
- **RENAME** and **DELETE** act on the active theme. Deleting it switches back to DEFAULT.
- **COPY** gives the active theme's share code. When the browser allows it, the code goes straight
  to the clipboard; otherwise it is shown, selected, to copy by hand.
- **PASTE** imports a share code as a new theme and switches to it.
- **RESET ALL** puts every colour of the active theme back to its default.

Up to 20 themes can be saved.

### Themes folder

Theme files can also be dropped into the mod's themes folder, next to DOC's kneeboard folder:
`BepInEx/plugins/NOXMFD/themes/` (created automatically when the plugin loads). Each `.json` file
there shows as a theme card with a **FILE** tag. The folder is read when the game starts and again
every time the UI page opens, so a new or edited file appears the next time you open CFG > UI.

A folder theme is read-only, like DEFAULT: select it to use it, and changing a colour asks for a name
and saves a copy as a normal theme. To change the folder theme itself, edit its file. Deleting the
file removes the theme; if it was active, DEFAULT takes over. **COPY** still gives its share code.

A theme file names the theme and lists the colours it changes, using the names in
`src/web/pages/ui/ui-tokens.js`; any colour it leaves out keeps its default:

```json
{
  "name": "Dusk",
  "colors": {
    "--no-green-rgb": "#ff9e3d",
    "--no-bg": "#120c18"
  }
}
```

Values must be `#rrggbb`. A file without a `name` uses its file name; a file with no valid colour
is skipped and noted in the BepInEx log. Up to 50 files of 64 KB each are read.

### PREVIEW

Small mocks of the [TGT](tgt.md), [MAP](map.md) and [HSD](rdr.md#hsd) pages above the colour rows, each
in a grey MFD frame, drawn with the colours being edited. TGT shows the presets, both filter rows,
the vehicle-type lamps, a target list with focused, datalink and stale rows, the density toggle and
all four action buttons; MAP shows the grid, a route,
units of each faction, a target, a nuclear zone, a jamming line and the readout chips; HSD shows
the range rings, the radar cone, a route, an AA threat ring and contacts in each state. TGT spans the
full width with MAP and HSD side by side below it (all stacked on a narrow screen), and they follow
a picker or hex value as it changes, before the change is applied.

### Colour rows

Each row is one colour: tap the swatch to pick a new one, or type a hex value (`#rrggbb`, the `#`
optional) and press Enter. **↺** puts that colour back to its default. Rows are grouped as CORE PALETTE, ACCENTS, THREATS, and MAP & SCOPE. Changing a base
colour such as PRIMARY GREEN also recolours its dimmer and darker shades.
