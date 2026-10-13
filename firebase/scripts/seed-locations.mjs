import { loadCatalogue, documentFields, parseOptions, validateTarget } from './catalogue.mjs';

// Dry-run is the default and does not import the Admin SDK or contact Firebase.
async function main() {
  const options = parseOptions(process.argv.slice(2));
  const data = loadCatalogue();
  const emulatorHost = process.env.FIRESTORE_EMULATOR_HOST;
  validateTarget(options, emulatorHost);
  console.log(`Project: ${options.project || '(not selected; dry-run only)'}`);
  console.log(`Target: ${emulatorHost ? `local emulator ${emulatorHost}` : 'Cloud Firestore'}`);
  console.log(`Mode: ${options.apply ? (options.overwrite ? 'APPLY / merge known fields into existing documents' : 'APPLY / create missing documents only') : 'DRY RUN / no writes'}`);
  for (const item of data) console.log(`  locations/${item.id} - ${item.name}`);
  if (!options.apply) {
    console.log('No documents were written. Use --project PROJECT_ID --apply after reviewing the data.');
    return;
  }
  const { initializeApp, applicationDefault, deleteApp } = await import('firebase-admin/app');
  const { getFirestore } = await import('firebase-admin/firestore');
  const app = initializeApp(emulatorHost
    ? { projectId: options.project }
    : { projectId: options.project, credential: applicationDefault() });
  const db = getFirestore(app);
  try {
    const refs = data.map(item => db.collection('locations').doc(item.id));
    const written = await db.runTransaction(async transaction => {
      const existing = await transaction.getAll(...refs);
      let count = 0;
      data.forEach((item, index) => {
        if (!existing[index].exists) {
          transaction.create(refs[index], documentFields(item)); count++;
        } else if (options.overwrite) {
          transaction.set(refs[index], documentFields(item), { merge: true }); count++;
        }
      });
      return count;
    });
    console.log(`Committed ${written} document writes. Existing documents were ${options.overwrite ? 'explicitly updated' : 'left unchanged'}.`);
    console.log('The seeder never deletes documents.');
  } finally {
    await db.terminate();
    await deleteApp(app);
  }
}
main().catch(error => { console.error(`Seeding stopped: ${error.message}`); process.exitCode = 1; });
