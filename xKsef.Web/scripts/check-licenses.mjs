import { existsSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const lock = JSON.parse(readFileSync(join(root, 'package-lock.json'), 'utf8'));
const allowed = new Set(['MIT', 'Apache-2.0', 'MPL-2.0', 'ISC', 'BSD-3-Clause', '0BSD']);
const packages = Object.entries(lock.packages).filter(([path]) => path !== '');
const rejected = packages.filter(([, metadata]) => !allowed.has(metadata.license));
if (rejected.length) throw new Error(`Review dependency licences: ${rejected.map(([path, metadata]) => `${path}: ${metadata.license}`).join(', ')}`);

const runtime = packages.filter(([, metadata]) => !metadata.dev);
const sections = runtime.map(([path, metadata]) => {
  const directory = resolve(root, path);
  if (!directory.startsWith(join(root, 'node_modules') + sep)) throw new Error('Unexpected dependency path.');
  const manifest = JSON.parse(readFileSync(join(directory, 'package.json'), 'utf8'));
  const licenseFile = ['LICENSE', 'LICENSE.txt', 'LICENSE.md'].map(name => join(directory, name)).find(existsSync);
  if (!licenseFile || manifest.version !== metadata.version) throw new Error(`Run npm ci and review licence for ${path}.`);
  return `=== ${manifest.name}@${manifest.version} (${metadata.license}) ===\n${readFileSync(licenseFile, 'utf8').trim()}\n`;
});
const notices = `Third-party software distributed with xKsef.Web\nThis file covers runtime dependencies; it does not license the xKsef application itself.\n\n${sections.join('\n')}`;
const output = join(root, 'public', 'third-party-notices.txt');
if (process.argv.includes('--write-notices')) writeFileSync(output, notices, 'utf8');
if (!existsSync(output) || readFileSync(output, 'utf8') !== notices) throw new Error('Update notices using npm run licenses:write and review the result.');
console.log(`Licences OK: ${packages.length} lockfile dependencies (including optional platforms), ${runtime.length} runtime dependencies.`);
console.log(`Runtime: ${[...new Set(runtime.map(([, metadata]) => metadata.license))].join(', ')}. Tooling: ${[...new Set(packages.map(([, metadata]) => metadata.license))].sort().join(', ')}.`);
