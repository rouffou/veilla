import { loadRuntimeConfig, parseRuntimeConfig, RuntimeConfigError } from './runtime-config';

describe('parseRuntimeConfig', () => {
  it('accepte une configuration complète et normalise les URL', () => {
    const config = parseRuntimeConfig({
      apiBaseUrl: 'https://api.example.test/bff/',
      auth: { authority: 'https://idp.example.test/realms/veilla/', clientId: 'x', scope: 'openid' },
    });
    expect(config.apiBaseUrl).toBe('https://api.example.test/bff');
    expect(config.auth.authority).toBe('https://idp.example.test/realms/veilla');
  });

  it('signale les clés manquantes', () => {
    expect(() => parseRuntimeConfig({ auth: { clientId: 'x' } })).toThrowError(
      /apiBaseUrl, auth\.authority, auth\.scope/,
    );
    expect(() => parseRuntimeConfig(null)).toThrowError(RuntimeConfigError);
  });
});

describe('loadRuntimeConfig', () => {
  it('rejette si le fichier est absent', async () => {
    const fakeFetch = (async () => new Response('', { status: 404 })) as typeof fetch;
    await expect(loadRuntimeConfig('assets/config.json', fakeFetch)).rejects.toThrowError(/HTTP 404/);
  });

  it('charge et valide le fichier', async () => {
    const body = JSON.stringify({
      apiBaseUrl: '/api',
      auth: { authority: 'https://idp.test', clientId: 'c', scope: 'openid' },
    });
    const fakeFetch = (async () => new Response(body, { status: 200 })) as typeof fetch;
    const config = await loadRuntimeConfig('assets/config.json', fakeFetch);
    expect(config.auth.clientId).toBe('c');
  });
});
