import { describe, expect, it } from 'vitest';
import { inspectDiagnostic, isDiagnosticFile } from './check-diagnostic-secrets';

describe('diagnostic secret guard', () => {
  it.each(['a.http', 'a.rest', 'a.har', 'a.curl', 'tools/curl-api.ps1', 'http-client.env.json', 'collection.postman.json'])('includes diagnostic artifact %s', file => {
    expect(isDiagnosticFile(file)).toBe(true);
  });
  it('accepts Visual Studio environment references without embedding credentials', () => {
    expect(inspectDiagnostic('a.http', 'Authorization: Bearer {{$processEnv API_TOKEN}}\n{"password":"{{$processEnv API_PASSWORD}}"}\n@token = {{login.response.body.$.token}}')).toEqual([]);
  });
  it.each([
    '{"password":"synthetic-value"}', '@apiKey = synthetic-value', 'Authorization: Bearer synthetic-value',
    'Cookie: session=synthetic-value', '/api?token=synthetic-value', '-----BEGIN PRIVATE KEY-----',
    '{"log":{"entries":[{"name":"Authorization","value":"Bearer synthetic-value"}]}}',
    '{"header":[{"key":"apiKey","value":"synthetic-value"}]}',
    ['eyJhbGciOiJub25lIn0', 'eyJleHAiOjF9', 'synthetic'].join('.'),
  ])('rejects a synthetic credential without including its value in diagnostics (%#)', text => {
    const result = inspectDiagnostic('fixture.http', text); expect(result.length).toBeGreaterThan(0);
    expect(JSON.stringify(result)).not.toContain('synthetic-value'); expect(result.every(item => item.file === 'fixture.http' && item.line >= 1)).toBe(true);
  });
});
