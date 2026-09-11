const assert = require('assert');
const { signature } = require('./map-color-policy.js');

const base = {
  n: '#999999', f: '#00ff00', e: '#ff0000',
  types: {
    Truck: { hex: '#123456' },
    SAM: { hex: '#abcdef', f: 2 },
  },
};

assert.strictEqual(signature(base), signature({
  n: '#999999', f: '#00ff00', e: '#ff0000',
  types: {
    SAM: { f: 2, hex: '#abcdef' },
    Truck: { hex: '#123456' },
  },
}), 'dictionary and property order must not invalidate the tint cache');

assert.notStrictEqual(signature(base), signature({ ...base, e: '#ff00ff' }),
  'a faction-color change must invalidate the tint cache');
assert.notStrictEqual(signature(base), signature({
  ...base,
  types: { ...base.types, SAM: { hex: '#abcdef', f: 1 } },
}), 'a type faction-filter change must invalidate the tint cache');
assert.notStrictEqual(signature(base), signature({
  ...base,
  types: { ...base.types, SAM: { hex: '#fedcba', f: 2 } },
}), 'a type-color change must invalidate the tint cache');

console.log('map-color-policy.test.js: OK');
