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
