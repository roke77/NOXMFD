// Fixed squad-callsign list (issue #42) — replaces the free-text callsign field with a picker so
// every squad name matches a real military callsign convention instead of whatever a pilot types.
// Sourced from the DCS World callsigns reference (GitHub issue #42's own comment), flattened across
// every aircraft/role category and deduped — this module only cares about the name itself, not
// which aircraft type a callsign was originally associated with. Kept alphabetical so the SQD
// page's <select> is scannable, not grouped by source category.
export const SQUAD_CALLSIGNS = [
  'ANVIL', 'APACHE', 'ARCO', 'ARMY AIR', 'ASCOT', 'AXEMAN', 'BADGER', 'BEST', 'BOAR', 'BONE',
  'BOOTLEG', 'BUFF', 'CARGO', 'CARNIVOR', 'CHECK', 'CHEVY', 'COLT', 'COWBOY', 'CROW', 'DARK',
  'DARKNIGHT', 'DARKSTAR', 'DEATHSTAR', 'DEVIL', 'DODGE', 'DUDE', 'DUMP', 'ENFIELD', 'EYEBALL',
  'FERRET', 'FINGER', 'FIREFLY', 'FOCUS', 'FORD', 'GATLING', 'GUNNY', 'GUNSLINGER', 'HAMMER',
  'HAMMERHEAD', 'HAWG', 'HAWK', 'HEAVY', 'HORNET', 'JAGUAR', 'JAZZ', 'JEDI', 'JOKER', 'JURY',
  'KENWORTH', 'LOBO', 'MAGIC', 'MANTIS', 'MOONBEAM', 'NINJA', 'OVERLORD', 'PALEHORSE', 'PANTHER',
  'PIG', 'PINPOINT', 'PLAYBOY', 'POINTER', 'PONTIAC', 'PYTHON', 'RAGE', 'RAGIN', 'RAM', 'RATTLER',
  'ROMAN', 'SABER', 'SCORPION', 'SHABA', 'SHELL', 'SIOUX', 'SLED', 'SNAKE', 'SNIPER', 'SPRINGFIELD', 'SQUID',
  'STING', 'TAHOE', 'TEXACO', 'THUD', 'TRASH', 'TREK', 'TUSK', 'UZI', 'VADER', 'VENOM', 'VIPER',
  'WARRIOR', 'WEASEL', 'WHIPLASH', 'WILD', 'WIZARD', 'WOLF',
];
