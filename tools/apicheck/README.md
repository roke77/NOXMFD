# apicheck — reflection guard for game updates

The plugin reaches into Nuclear Option's private internals by reflection
(`typeof(T).GetField("name")`, …). Those calls compile no matter what the game
does; they break only at **runtime**, silently, when an update renames or
retypes the member — the feature just stops working, no compiler error, often no
log. That's the exact failure mode a game update tends to cause.

`apicheck` closes that blind spot. It scans the plugin source for every
`typeof(T).GetField/GetMethod/GetProperty("name")` call and resolves each against
the **current** `Assembly-CSharp.dll` via `MetadataLoadContext` (metadata only —
nothing is executed), reporting any member that vanished or changed type.

## Run it after every game update

```bash
dotnet run --project tools/apicheck
```

With no argument it reads the game location from `GameDir.props` (the same
gitignored file the plugin build uses). Or pass one explicitly:

```bash
dotnet run --project tools/apicheck -- "D:\path\to\Nuclear Option"
```

Exit code is `0` when everything resolves, `1` when a member is missing or
retyped (so it fits a pre-release check). The reflected list is pulled straight
from the source, so it never drifts from the code.

## What it does NOT cover

- **Dynamic sites** — `someObj.GetType().GetField(...)` where the type isn't a
  literal `typeof`. These are listed at the end of the run for manual review.
- **Behaviour / index drift** — a member that still exists with the same type but
  is populated or ordered differently (e.g. HUD category indices, countermeasure
  or faction enums). That needs an in-game check, not this tool.
