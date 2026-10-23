// Self-check for the classic MAIN card's URL rows. Run: `node main-urls.test.js`.
const assert = require('assert');
const { plan } = require('./main-urls.js');

const local = 'http://localhost:5005', lan = 'http://192.168.1.61:5005', name = 'http://noxmfd.local:5005';

// A name URL is the only row; PC and NET wait behind the hint.
assert.deepStrictEqual(plan({ localhost: local, lanUrl: lan, nameUrl: name }),
  { urls: [name], alts: [{ label: 'PC', url: local }, { label: 'NET', url: lan }] });
// No IP detected: the hint still offers localhost.
assert.deepStrictEqual(plan({ localhost: local, nameUrl: name }), { urls: [name], alts: [{ label: 'PC', url: local }] });
// No name URL: the plain addresses are the rows, no hint.
assert.deepStrictEqual(plan({ localhost: local, lanUrl: lan }), { urls: [local, lan], alts: [] });
// /config unreachable: the default localhost row.
assert.deepStrictEqual(plan(null), { urls: [local], alts: [] });

console.log('main-urls: all assertions passed');
