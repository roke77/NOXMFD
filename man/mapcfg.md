# MAP — CFG

[MAP](map.md)'s own settings page, reached from MAP's CFG key. Two live-adjustable sliders and a
toggle.

## TLM

The main telemetry tick — own-ship state, weapons, RWR/MW, [TGT](tgt.md) filters, and faction
stats. Everything else on this mod's other pages rides this one rate, MAP included. Defaults to
10 Hz. Higher rates cost more CPU/GPU and network bandwidth; lower rates save it at the cost of
smoothness and latency. Changes apply immediately and persist across restarts.

## CONTACTS

The [MAP](map.md)/[RDR](rdr.md)/HSD contact and pitbull-missile refresh rate, split off TLM since
full contact lists cost more per tick than own-ship state does. Defaults to 4 Hz. Same
cost/smoothness tradeoff as TLM, and changes apply immediately and persist the same way.

## SHOW PLAYER NAMES

When ON, shows a pilot name label above every player-controlled aircraft on [MAP](map.md) —
friendly and enemy alike, wherever it's already visible as a contact. AI-flown aircraft never get
a label. Off by default; persists across restarts like the sliders above.

## RESET TO DEFAULT

Restores TLM to 10 Hz, CONTACTS to 4 Hz, and SHOW PLAYER NAMES to off.
