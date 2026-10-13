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
  });
});
after(async () => { if (environment) await environment.cleanup(); });
const visitor = () => environment.authenticatedContext('visitor', {
  firebase: { sign_in_provider: 'anonymous', identities: {} }
}).firestore();
const guest = () => environment.unauthenticatedContext().firestore();

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
