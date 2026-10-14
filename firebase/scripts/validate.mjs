import { loadCatalogue } from './catalogue.mjs';
try {
  const data = loadCatalogue();
  console.log(`VALID: ${data.length} unique locations; bundled JSON and seed data match.`);
  for (const item of data) console.log(`locations/${item.id}: ${item.name} (${item.mapSection || 'map position not supplied'})`);
} catch (error) { console.error(error.message); process.exitCode = 1; }
