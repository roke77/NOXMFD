// TRACK ON MAP (docs/atc-extension-support.md item 3 follow-on): map.js is a browser composition
// root (DOM/canvas setup at module scope), so this follows mfd-split-routing.test.js's source-scan
// approach rather than requiring the file. Guards what a regression here would silently
// break: engaging track drops FLW, the pan-centering branch actually reads the tracked unit (and
// takes priority over player-follow), a mission end resets the tracking flag, and LOCATE ON MAP's
// one-shot center (armed per new count, ahead of both follow modes, dropping FLW).
// Run: `node map-track-follow.test.js`.
const assert = require('assert');
const fs = require('fs');
const path = require('path');

const source = fs.readFileSync(path.join(__dirname, 'map.js'), 'utf8');

const renderStart = source.indexOf('function renderFrame(d)');
const renderEnd = source.indexOf('\nfunction handleNoMission', renderStart);
assert.ok(renderStart >= 0 && renderEnd > renderStart, 'could not isolate renderFrame');
const renderFrame = source.slice(renderStart, renderEnd);

assert.ok(renderFrame.includes('d.selectedUnitTrack && !trackingSelectedUnit'),
  'renderFrame must detect TRACK ON MAP engaging');
assert.ok(renderFrame.includes('if (followPlayer) setFollow(false)'),
  'engaging TRACK ON MAP must drop FLW, the checkbox\'s own promise');

const drawStart = source.indexOf('function drawOverlay()');
const drawEnd = source.indexOf('\n// ', source.indexOf('Blit the map sprite', drawStart));
assert.ok(drawStart >= 0 && drawEnd > drawStart, 'could not isolate drawOverlay\'s pan section');
const panSection = source.slice(drawStart, drawEnd);

const trackBranch = panSection.indexOf('if (trackingSelectedUnit');
const followBranch = panSection.indexOf('followPlayer && view.zoom > MIN_ZOOM && lastData.world');
assert.ok(trackBranch >= 0, 'drawOverlay must have a tracking-unit pan branch');
assert.ok(followBranch >= 0 && followBranch > trackBranch,
  'TRACK ON MAP must be checked before (take priority over) player-follow');
assert.ok(panSection.includes('lastData.contacts.find(function(u) { return u.id === lastData.selectedUnitId; })'),
  'the tracking branch must actually look up the selected unit among live contacts');

// LOCATE ON MAP's one-shot center (Api.LocateOnMap): a new count arms it, the first frame only
// records the count, and the center runs before (and so beats) both follow modes, dropping FLW.
assert.ok(renderFrame.includes('d.selectedUnitCenter !== centerSeq') && renderFrame.includes('if (centerSeq !== null) centerPending = true'),
  'renderFrame must arm a center on a new selectedUnitCenter, but not on the first frame');
const centerBranch = panSection.indexOf('if (centerPending)');
assert.ok(centerBranch >= 0 && centerBranch < trackBranch, 'the one-shot center must run before the follow modes');
assert.ok(panSection.slice(centerBranch, trackBranch).includes('followPlayer = false'),
  'a LOCATE ON MAP center must drop FLW, or player-follow pulls the view straight back');

const clearStart = source.indexOf('function clearViewState()');
const clearEnd = source.indexOf('\n}', clearStart);
assert.ok(clearStart >= 0 && clearEnd > clearStart, 'could not isolate clearViewState');
assert.ok(source.slice(clearStart, clearEnd).includes('trackingSelectedUnit = false'),
  'a mission end must reset TRACK ON MAP, same as it resets FLW');
assert.ok(source.slice(clearStart, clearEnd).includes('centerPending = false'),
  'a mission end must drop a LOCATE ON MAP center not yet drawn');

console.log('map-track-follow.test.js: OK');
