import test, { before, after } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { initializeTestEnvironment, assertFails, assertSucceeds } from '@firebase/rules-unit-testing';
import { collection, doc, getDoc, getDocs, setDoc, updateDoc, deleteDoc } from 'firebase/firestore';
import { loadCatalogue, documentFields } from '../scripts/catalogue.mjs';

let environment;
const data = loadCatalogue();
before(async () => {
  environment = await initializeTestEnvironment({
    projectId: 'demo-bcopenday-members34',
    firestore: { rules: readFileSync(new URL('../firestore.rules', import.meta.url), 'utf8') }
  });
  await environment.clearFirestore();
  await environment.withSecurityRulesDisabled(async context => {
    const db = context.firestore();
    for (const item of data) await setDoc(doc(db, 'locations', item.id), documentFields(item));
    await setDoc(doc(db, 'users', 'visitor'), { name: 'Private visitor' });
    await setDoc(doc(db, 'locations', 'library', 'private', 'note'), { internal: true });
    await setDoc(doc(db, 'scanners', 'staff'), { name: 'Scanner staff' });
    await setDoc(doc(db, 'buildings', 'main'), building('staff'));
    await setDoc(doc(db, 'panoramas', PANO_ID), panorama('staff'));
    await setDoc(doc(db, 'panoramas', PANO_ID, 'chunks', '0'), chunk(0));
  });
});
after(async () => { if (environment) await environment.cleanup(); });
const visitor = () => environment.authenticatedContext('visitor', {
  firebase: { sign_in_provider: 'anonymous', identities: {} }
}).firestore();
const guest = () => environment.unauthenticatedContext().firestore();
const staff = () => environment.authenticatedContext('staff', {
  firebase: { sign_in_provider: 'password', identities: {} }
}).firestore();
const unlistedPasswordUser = () => environment.authenticatedContext('stranger', {
  firebase: { sign_in_provider: 'password', identities: {} }
}).firestore();
// A visitor whose ANONYMOUS uid happens to match a scanner entry must still not write.
const anonymousWithStaffUid = () => environment.authenticatedContext('staff', {
  firebase: { sign_in_provider: 'anonymous', identities: {} }
}).firestore();
const PANO_ID = 'a'.repeat(32);
function panorama(uid, overrides = {}) {
  return {
    name: 'Library entrance', width: 4096, height: 711, verticalFov: 62.5, isFullSphere: false,
    captureMethod: 'in-app-capture', photoCount: 10, hasLocation: true, lat: -25.7545, lng: 28.1879,
    accuracyMeters: 4.5, hasNorthOffset: true, northOffset: 0.5, mimeType: 'image/jpeg', chunkCount: 1,
    byteLength: 3, sha256: 'b'.repeat(64), uploadId: 'c'.repeat(32), updatedAtUnixMs: 1760000000000,
    updatedBy: uid, ...overrides
  };
}
function chunk(index, overrides = {}) {
  return { data: 'AAAA', uploadId: 'c'.repeat(32), index, ...overrides };
}
function building(uid, overrides = {}) {
  return {
    name: 'Main Building', summary: 'Reception and lecture halls', description: '', stopId: '',
    centerLat: -25.7, centerLng: 28.2, radiusMeters: 25, footprint: '', isActive: true,
    updatedAtUnixMs: 1760000000000, updatedBy: uid, ...overrides
  };
}

test('anonymous authenticated visitor can read one location', async () => {
  await assertSucceeds(getDoc(doc(visitor(), 'locations', 'library')));
});
test('anonymous authenticated visitor can list all five locations', async () => {
  const result = await assertSucceeds(getDocs(collection(visitor(), 'locations')));
  assert.equal(result.size, 5);
});
test('signed-out visitor cannot read locations', async () => {
  await assertFails(getDoc(doc(guest(), 'locations', 'library')));
});
test('signed-out visitor cannot list locations', async () => {
  await assertFails(getDocs(collection(guest(), 'locations')));
});
test('anonymous authenticated visitor cannot create locations', async () => {
  await assertFails(setDoc(doc(visitor(), 'locations', 'injected'), documentFields(data[0])));
});
test('anonymous authenticated visitor cannot update a location', async () => {
  await assertFails(updateDoc(doc(visitor(), 'locations', 'library'), { name: 'Tampered' }));
});
test('anonymous authenticated visitor cannot delete a location', async () => {
  await assertFails(deleteDoc(doc(visitor(), 'locations', 'library')));
});
test('signed-out visitor cannot write', async () => {
  await assertFails(setDoc(doc(guest(), 'locations', 'injected'), documentFields(data[0])));
});
test('client admin claims do not grant catalogue writes', async () => {
  const adminClient = environment.authenticatedContext('claimed-admin', { admin: true }).firestore();
  await assertFails(updateDoc(doc(adminClient, 'locations', 'library'), { name: 'Tampered' }));
});
test('parent read permission does not leak subcollection documents', async () => {
  await assertFails(getDoc(doc(visitor(), 'locations', 'library', 'private', 'note')));
});
test('unrelated user documents remain protected even for the same uid', async () => {
  await assertFails(getDoc(doc(visitor(), 'users', 'visitor')));
});
test('unrelated collection writes are denied', async () => {
  await assertFails(setDoc(doc(visitor(), 'users', 'visitor'), { name: 'Changed' }));
});

test('visitors can read buildings', async () => {
  await assertSucceeds(getDocs(collection(visitor(), 'buildings')));
});
test('signed-out users cannot read buildings', async () => {
  await assertFails(getDocs(collection(guest(), 'buildings')));
});
test('a listed scanner account can create a valid building', async () => {
  await assertSucceeds(setDoc(doc(staff(), 'buildings', 'library-block'), building('staff')));
});
test('a listed scanner account can delete a building', async () => {
  await assertSucceeds(setDoc(doc(staff(), 'buildings', 'to-delete'), building('staff')));
  await assertSucceeds(deleteDoc(doc(staff(), 'buildings', 'to-delete')));
});
test('anonymous visitors cannot write buildings', async () => {
  await assertFails(setDoc(doc(visitor(), 'buildings', 'fake'), building('visitor')));
  await assertFails(updateDoc(doc(visitor(), 'buildings', 'main'), { name: 'Tampered' }));
  await assertFails(deleteDoc(doc(visitor(), 'buildings', 'main')));
});
test('password accounts NOT on the scanner list cannot write buildings', async () => {
  await assertFails(setDoc(doc(unlistedPasswordUser(), 'buildings', 'fake'), building('stranger')));
});
test('an anonymous session with a scanner uid cannot write buildings', async () => {
  await assertFails(setDoc(doc(anonymousWithStaffUid(), 'buildings', 'fake'), building('staff')));
});
test('scanner writes must carry their own uid and valid values', async () => {
  await assertFails(setDoc(doc(staff(), 'buildings', 'bad-uid'), building('someone-else')));
  await assertFails(setDoc(doc(staff(), 'buildings', 'bad-lat'), building('staff', { centerLat: 123 })));
  await assertFails(setDoc(doc(staff(), 'buildings', 'bad-radius'), building('staff', { radiusMeters: 9999 })));
  await assertFails(setDoc(doc(staff(), 'buildings', 'Bad_ID'), building('staff')));
});
test('a scanner can read only its own allow-list entry', async () => {
  await assertSucceeds(getDoc(doc(staff(), 'scanners', 'staff')));
  await assertFails(getDoc(doc(visitor(), 'scanners', 'staff')));
  await assertFails(getDocs(collection(staff(), 'scanners')));
});
test('nobody can add themselves to the scanner list from the app', async () => {
  await assertFails(setDoc(doc(unlistedPasswordUser(), 'scanners', 'stranger'), { name: 'Me' }));
  await assertFails(setDoc(doc(staff(), 'scanners', 'staff'), { name: 'Changed' }));
});

// ---- 360° views (panoramas + their image pieces) ----------------------------------------
test('visitors can read 360° views and their image pieces', async () => {
  await assertSucceeds(getDoc(doc(visitor(), 'panoramas', PANO_ID)));
  await assertSucceeds(getDocs(collection(visitor(), 'panoramas')));
  await assertSucceeds(getDoc(doc(visitor(), 'panoramas', PANO_ID, 'chunks', '0')));
});
test('signed-out users cannot read 360° views', async () => {
  await assertFails(getDoc(doc(guest(), 'panoramas', PANO_ID)));
  await assertFails(getDoc(doc(guest(), 'panoramas', PANO_ID, 'chunks', '0')));
});
test('a listed scanner account can upload and remove a 360° view', async () => {
  const id = 'd'.repeat(32);
  await assertSucceeds(setDoc(doc(staff(), 'panoramas', id, 'chunks', '0'), chunk(0)));
  await assertSucceeds(setDoc(doc(staff(), 'panoramas', id, 'chunks', '15'), chunk(15)));
  await assertSucceeds(setDoc(doc(staff(), 'panoramas', id), panorama('staff')));
  await assertSucceeds(deleteDoc(doc(staff(), 'panoramas', id)));
  await assertSucceeds(deleteDoc(doc(staff(), 'panoramas', id, 'chunks', '0')));
});
test('visitors and unlisted accounts cannot upload or remove 360° views', async () => {
  await assertFails(setDoc(doc(visitor(), 'panoramas', 'e'.repeat(32)), panorama('visitor')));
  await assertFails(setDoc(doc(visitor(), 'panoramas', PANO_ID, 'chunks', '1'), chunk(1)));
  await assertFails(deleteDoc(doc(visitor(), 'panoramas', PANO_ID)));
  await assertFails(deleteDoc(doc(visitor(), 'panoramas', PANO_ID, 'chunks', '0')));
  await assertFails(setDoc(doc(unlistedPasswordUser(), 'panoramas', 'e'.repeat(32)), panorama('stranger')));
  await assertFails(setDoc(doc(anonymousWithStaffUid(), 'panoramas', PANO_ID, 'chunks', '1'), chunk(1)));
});
test('360° view documents must be valid', async () => {
  await assertFails(setDoc(doc(staff(), 'panoramas', 'not-a-hex-id'), panorama('staff')));
  await assertFails(setDoc(doc(staff(), 'panoramas', 'f'.repeat(32)), panorama('someone-else')));
  await assertFails(setDoc(doc(staff(), 'panoramas', 'f'.repeat(32)), panorama('staff', { chunkCount: 17 })));
  await assertFails(setDoc(doc(staff(), 'panoramas', 'f'.repeat(32)), panorama('staff', { lat: 123 })));
  await assertFails(setDoc(doc(staff(), 'panoramas', 'f'.repeat(32)), panorama('staff', { mimeType: 'text/html' })));
  await assertFails(setDoc(doc(staff(), 'panoramas', 'f'.repeat(32)), panorama('staff', { extra: 1 })));
});
test('360° image pieces must be numbered 0-15, sized and labelled correctly', async () => {
  await assertFails(setDoc(doc(staff(), 'panoramas', PANO_ID, 'chunks', '16'), chunk(16)));
  await assertFails(setDoc(doc(staff(), 'panoramas', PANO_ID, 'chunks', '2'), chunk(3)));
  await assertFails(setDoc(doc(staff(), 'panoramas', PANO_ID, 'chunks', '2'), chunk(2, { data: 'A'.repeat(700001) })));
  await assertFails(setDoc(doc(staff(), 'panoramas', PANO_ID, 'chunks', '2'), chunk(2, { extra: true })));
});
test('360° views can name their campus location and description', async () => {
  const id = '9'.repeat(32);
  await assertSucceeds(setDoc(doc(staff(), 'panoramas', id), panorama('staff', { locationId: 'library', description: 'Study rooms' })));
  await assertSucceeds(setDoc(doc(staff(), 'panoramas', id), panorama('staff', { locationId: '' })));
  await assertFails(setDoc(doc(staff(), 'panoramas', id), panorama('staff', { locationId: 'Not Valid!' })));
  await assertFails(setDoc(doc(staff(), 'panoramas', id), panorama('staff', { description: 'x'.repeat(301) })));
});

function locationPhoto(uid, overrides = {}) {
  return { data: 'AAAA', sha256: 'b'.repeat(64), mimeType: 'image/jpeg', updatedAtUnixMs: 1760000000000, updatedBy: uid, ...overrides };
}
test('visitors can read a location photo but not list or change them', async () => {
  await assertSucceeds(getDoc(doc(visitor(), 'locationPhotos', 'library')));
  await assertFails(getDoc(doc(guest(), 'locationPhotos', 'library')));
  await assertFails(getDocs(collection(visitor(), 'locationPhotos')));
  await assertFails(setDoc(doc(visitor(), 'locationPhotos', 'library'), locationPhoto('visitor')));
  await assertFails(setDoc(doc(unlistedPasswordUser(), 'locationPhotos', 'library'), locationPhoto('stranger')));
});
test('a listed scanner account can set and remove a valid location photo', async () => {
  await assertSucceeds(setDoc(doc(staff(), 'locationPhotos', 'library'), locationPhoto('staff')));
  await assertSucceeds(deleteDoc(doc(staff(), 'locationPhotos', 'library')));
  await assertFails(setDoc(doc(staff(), 'locationPhotos', 'Bad Id'), locationPhoto('staff')));
  await assertFails(setDoc(doc(staff(), 'locationPhotos', 'library'), locationPhoto('someone-else')));
  await assertFails(setDoc(doc(staff(), 'locationPhotos', 'library'), locationPhoto('staff', { data: 'A'.repeat(800001) })));
  await assertFails(setDoc(doc(staff(), 'locationPhotos', 'library'), locationPhoto('staff', { mimeType: 'image/png' })));
});
