import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

export interface DiagnosticFinding { readonly file: string; readonly line: number; readonly rule: string }
const sensitive = /^(?:password|passwd|token|accessToken|refreshToken|signingKey|apiKey|clientSecret|privateKey|secret|authorization|cookie)$/i;
const reference = (value: string) => /^\{\{[^{}\r\n]+\}\}$/.test(value.trim());
export const isDiagnosticFile = (file: string) => /\.(?:http|rest|har|curl)$/i.test(file)
  || /(?:postman|http-client|httpenv).*\.json(?:\.user)?$/i.test(file)
  || /(?:^|\/)(?:curl|requests?|http)[^/]*\.(?:ps1|sh|cmd|bat|txt|json)$/i.test(file);

/** Findings contain only locations and rule names, never matched credential values. */
export function inspectDiagnostic(file: string, text: string): DiagnosticFinding[] {
  const findings: DiagnosticFinding[] = [];
  const add = (line: number, rule: string) => findings.push({ file, line, rule });
  text.split(/\r?\n/).forEach((line, index) => {
    if (/eyJ[\w-]+\.[\w-]+\.[\w-]+/.test(line)) add(index + 1, 'literal JWT');
    if (/-----BEGIN (?:[A-Z]+ )?PRIVATE KEY-----/.test(line)) add(index + 1, 'private key');
    const assignments = [
      ...line.matchAll(/"([\w-]+)"\s*:\s*"([^"\r\n]*)"/g),
      ...line.matchAll(/^\s*@?([\w-]+)\s*=\s*(.*?)\s*$/g),
      ...line.matchAll(/(?:^|[?&])([\w-]+)=([^&\s]*)/g),
    ];
    for (const match of assignments) {
      if (sensitive.test(match[1].replace(/[-_]/g, '')) && match[2] && !reference(match[2])) add(index + 1, 'literal credential');
    }
    const header = /^\s*(Authorization|Cookie|X-Api-Key)\s*:\s*(.*?)\s*$/i.exec(line);
    if (header && !reference(header[2].replace(/^(?:Bearer|Basic)\s+/i, ''))) add(index + 1, 'literal authentication header');
  });
  // HAR/Postman headers store the credential name and value as separate JSON fields.
  try {
    const walk = (value: unknown) => {
      if (!value || typeof value !== 'object') return;
      if (Array.isArray(value)) { value.forEach(walk); return; }
      const object = value as Record<string, unknown>;
      const name = object.name ?? object.key;
      if (typeof name === 'string' && sensitive.test(name.replace(/[-_]/g, '')) && typeof object.value === 'string'
          && object.value && !reference(object.value.replace(/^(?:Bearer|Basic)\s+/i, ''))) add(1, 'literal structured credential');
      Object.values(object).forEach(walk);
    };
    walk(JSON.parse(text));
  } catch { /* HTTP request files are not standalone JSON. */ }
  return findings;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  const root = execFileSync('git', ['rev-parse', '--show-toplevel'], { encoding: 'utf8', windowsHide: true }).trim();
  const files = [...new Set(execFileSync('git', ['ls-files', '--cached', '--others', '--exclude-standard', '-z'], { cwd: root, encoding: 'utf8', windowsHide: true }).split('\0'))].filter(isDiagnosticFile);
  const findings = files.flatMap(file => inspectDiagnostic(file, readFileSync(resolve(root, file), 'utf8')));
  for (const finding of findings) process.stderr.write(`${finding.file}:${finding.line}: ${finding.rule}\n`);
  process.stdout.write(`Diagnostic secret check: ${files.length} files, ${findings.length} findings.\n`);
  process.exitCode = findings.length ? 1 : 0;
}
