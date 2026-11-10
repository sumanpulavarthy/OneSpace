import { DOCUMENT } from '@angular/common';
import { TestBed } from '@angular/core/testing';
import { GraphqlClient } from '../../../core/graphql/graphql.client';
import { FakeGraphqlClient } from '../../../testing/fake-graphql-client';
import { ZohoConnectionStore, parseConnectOutcome } from './zoho-connection.store';

describe('ZohoConnectionStore', () => {
  let graphql: FakeGraphqlClient;
  let assign: jest.Mock;
  let store: ZohoConnectionStore;

  beforeEach(() => {
    graphql = new FakeGraphqlClient();
    assign = jest.fn();
    TestBed.configureTestingModule({
      providers: [
        ZohoConnectionStore,
        { provide: GraphqlClient, useValue: graphql },
        { provide: DOCUMENT, useValue: { location: { assign } } },
      ],
    });
    store = TestBed.inject(ZohoConnectionStore);
  });

  it('loads the connection status', async () => {
    graphql.on('ZohoConnection', () => ({ zohoConnection: { connected: true, connectedAt: '2026-11-10T00:00:00Z' } }));
    await store.load();
    expect(store.connected()).toBe(true);
  });

  it('sends the browser to the Zoho consent URL', async () => {
    graphql.on('ConnectZoho', () => ({ connectZoho: { authorizationUrl: 'https://auth.test/authorize?state=x' } }));
    await store.connect();
    expect(assign).toHaveBeenCalledWith('https://auth.test/authorize?state=x');
  });

  it('reports a friendly error when the connection cannot start', async () => {
    await expect(store.connect()).rejects.toThrow('Could not start the Zoho People connection');
    expect(store.busy()).toBe(false);
  });

  it('disconnect marks the store as not connected', async () => {
    graphql.on('DisconnectZoho', () => ({ disconnectZoho: true }));
    await store.disconnect();
    expect(store.connected()).toBe(false);
  });

  it('only accepts known outcomes from the query string', () => {
    expect(parseConnectOutcome('connected')).toBe('connected');
    expect(parseConnectOutcome('failed')).toBe('failed');
    expect(parseConnectOutcome('<script>')).toBeNull();
    expect(parseConnectOutcome(undefined)).toBeNull();
  });
});
