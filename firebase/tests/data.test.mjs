import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { loadCatalogue, validateLocations, documentFields, parseOptions, validateTarget, requiredIds } from '../scripts/catalogue.mjs';

const data = loadCatalogue();
const edited = mutate => { const copy = structuredClone(data); mutate(copy); return copy; };

test('five original stable IDs are preserved', () => assert.deepEqual(data.map(item => item.id), requiredIds));
test('bundled JSON equals Firestore seed data', () => assert.equal(loadCatalogue().length, 5));
test('document fields exclude the path ID', () => assert.equal('id' in documentFields(data[0]), false));
test('Library reference position is on the supplied main map', () => {
  const library = data.find(item => item.id === 'library');
  assert.equal(library.mapSection, 'main'); assert.ok(library.x > 0 && library.x < 1);
  assert.match(library.positionNote, /not a surveyed or GPS position/);
});
test('unprovided positions stay absent, not invented', () => {
  const lecture = data.find(item => item.id === 'lecture');
  assert.equal(lecture.mapSection, ''); assert.equal(lecture.x, null); assert.equal(lecture.y, null);
});
test('rejects an empty catalogue', () => assert.throws(() => validateLocations([])));
test('rejects duplicate IDs', () => assert.throws(() => validateLocations(edited(copy => copy[1].id = copy[0].id))));
test('rejects unknown IDs', () => assert.throws(() => validateLocations(edited(copy => copy[0].id = 'unknown'))));
test('rejects missing required fields', () => assert.throws(() => validateLocations(edited(copy => delete copy[0].description))));
test('rejects blank names', () => assert.throws(() => validateLocations(edited(copy => copy[0].name = ' '))));
test('rejects remote image URLs and path traversal', () => {
  for (const image of ['https://example.invalid/a.png', '../private.png'])
    assert.throws(() => validateLocations(edited(copy => copy[0].image = image)));
});
test('rejects incomplete coordinates', () => assert.throws(() => validateLocations(edited(copy => copy[2].x = 0.2))));
test('rejects NaN coordinates', () => assert.throws(() => validateLocations(edited(copy => copy[3].x = NaN))));
test('rejects coordinates outside the image', () => assert.throws(() => validateLocations(edited(copy => copy[3].y = 1.5))));
test('accepts real image boundary positions', () => assert.equal(validateLocations(edited(copy => { copy[3].x = 0; copy[3].y = 1; })).length, 5));
test('rejects non-integer order', () => assert.throws(() => validateLocations(edited(copy => copy[0].sortOrder = 1.5))));
test('dry-run is the default', () => assert.equal(parseOptions([]).apply, false));
test('cloud writes require an explicit project', () => assert.throws(() => parseOptions(['--apply'])));
test('overwrite requires explicit apply', () => assert.throws(() => parseOptions(['--overwrite'])));
test('placeholder projects are rejected', () => assert.throws(() => parseOptions(['--project', 'your-project-id'])));
test('unknown flags are rejected', () => assert.throws(() => parseOptions(['--force'])));
test('emulator refuses live project IDs', () => assert.throws(() => validateTarget({ apply: true, project: 'real-campus-project' }, 'localhost:8080')));
test('demo project cannot accidentally target the cloud', () => assert.throws(() => validateTarget({ apply: true, project: 'demo-campus' }, undefined)));
test('emulator refuses nonlocal endpoints', () => assert.throws(() => validateTarget({ apply: true, project: 'demo-campus' }, 'example.invalid:8080')));
test('explicit demo project plus local host is accepted', () => assert.doesNotThrow(() => validateTarget({ apply: true, project: 'demo-campus' }, '127.0.0.1:8080')));
test('C# Firestore DTO field names match the seeded schema', () => {
  const source = readFileSync(new URL('../../src/BCOpendayApp/Models/FirestoreHotspotDocument.cs', import.meta.url), 'utf8');
  const keys = [...source.matchAll(/FirestoreProperty\("([^"\n]+)"\)/g)].map(match => match[1]).sort();
  assert.deepEqual(keys, Object.keys(documentFields(data[0])).sort());
});
