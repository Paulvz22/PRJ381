import { readFileSync } from 'node:fs';

export const requiredIds = Object.freeze(['robotics', 'manufacturing', 'lecture', 'library', 'lounge']);
export const imageNames = Object.freeze(['roboticslab.png', 'manufacturinglab.png', 'lecturehall.png', 'library.png', 'studentlounge.png']);
const sections = new Set(['main', 'north', 'west']);
const keys = new Set(['id', 'name', 'image', 'summary', 'description', 'highlight', 'sortOrder', 'mapSection', 'x', 'y', 'positionNote']);

export function validateLocations(data) {
  if (!Array.isArray(data) || data.length !== requiredIds.length) {
    throw new Error('The catalogue must contain exactly five locations.');
  }
  const seen = new Set();
  for (const item of data) {
    if (!item || typeof item !== 'object' || Array.isArray(item)) throw new Error('Each location must be an object.');
    if (Object.keys(item).length !== keys.size || Object.keys(item).some(key => !keys.has(key))) {
      throw new Error('Location fields do not match the Hotspot schema.');
    }
    if (!requiredIds.includes(item.id) || seen.has(item.id)) throw new Error(`Unknown or duplicate id: ${item.id}`);
    seen.add(item.id);
    for (const [field, maximum] of [['name', 120], ['summary', 500], ['description', 5000], ['highlight', 500], ['positionNote', 1000]]) {
      if (typeof item[field] !== 'string' || !item[field].trim() || item[field].length > maximum) {
        throw new Error(`${item.id}: invalid ${field}`);
      }
    }
    if (!imageNames.includes(item.image)) throw new Error(`${item.id}: image must be a supplied resource name.`);
    if (!Number.isInteger(item.sortOrder) || item.sortOrder < 1 || item.sortOrder > 100) throw new Error(`${item.id}: invalid sortOrder`);
    const absent = item.mapSection === '' && item.x === null && item.y === null;
    const positioned = sections.has(item.mapSection) && [item.x, item.y].every(value =>
      typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= 1);
    if (!absent && !positioned) throw new Error(`${item.id}: use normalized image coordinates or an empty section and two nulls.`);
  }
  return data;
}

export function loadCatalogue() {
  const seed = JSON.parse(readFileSync(new URL('../locations.seed.json', import.meta.url), 'utf8'));
  const bundled = JSON.parse(readFileSync(new URL('../../MAUIProject/BCOpendayApp/Resources/Raw/locations.json', import.meta.url), 'utf8'));
  validateLocations(seed);
  validateLocations(bundled);
  if (JSON.stringify(seed) !== JSON.stringify(bundled)) {
    throw new Error('Seed and bundled JSON have drifted. Update both files before seeding/building.');
  }
  return seed;
}

export function documentFields(location) {
  // id belongs in the document path; the native DTO obtains it via FirestoreDocumentId.
  const { id, ...fields } = location;
  return fields;
}

export function parseOptions(args) {
  const options = { apply: false, overwrite: false, project: '' };
  for (let index = 0; index < args.length; index++) {
    const arg = args[index];
    if (arg === '--apply') options.apply = true;
    else if (arg === '--dry-run') options.apply = false;
    else if (arg === '--overwrite') options.overwrite = true;
    else if (arg === '--project') {
      const value = args[++index];
      if (!value || value.startsWith('--')) throw new Error('--project requires a project ID.');
      options.project = value;
    } else throw new Error(`Unknown option: ${arg}`);
  }
  if (options.project && (!/^[a-z][a-z0-9-]{4,28}[a-z0-9]$/.test(options.project)
      || /your-project|replace|placeholder/i.test(options.project))) throw new Error('Supply a real Firebase project ID (not a placeholder).');
  if (options.apply && !options.project) throw new Error('--apply requires an explicit --project PROJECT_ID.');
  if (options.overwrite && !options.apply) throw new Error('--overwrite also requires --apply.');
  return options;
}

export function validateTarget(options, emulatorHost) {
  if (!options.apply) return;
  if (emulatorHost && (!options.project.startsWith('demo-')
      || !/^(127\.0\.0\.1|localhost):[0-9]+$/.test(emulatorHost))) {
    throw new Error('Emulator writes require a demo- project ID and a local emulator host.');
  }
  if (!emulatorHost && options.project.startsWith('demo-')) throw new Error('A demo- project requires FIRESTORE_EMULATOR_HOST.');
}
