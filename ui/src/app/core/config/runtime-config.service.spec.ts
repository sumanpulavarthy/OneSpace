import { parseAzureAd } from './runtime-config.service';

describe('parseAzureAd', () => {
  it('returns null (dev mode) when config is missing or incomplete', () => {
    expect(parseAzureAd(null)).toBeNull();
    expect(parseAzureAd({ clientId: 'abc' })).toBeNull();
  });

  it('trims values and drops blank scopes', () => {
    expect(parseAzureAd({ clientId: ' c ', tenantId: ' t ', apiScopes: ['api://c/access_as_user', ' ', 5] })).toEqual({
      clientId: 'c',
      tenantId: 't',
      apiScopes: ['api://c/access_as_user'],
    });
  });
});
