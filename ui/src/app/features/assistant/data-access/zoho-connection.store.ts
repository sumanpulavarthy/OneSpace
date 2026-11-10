import { DOCUMENT } from '@angular/common';
import { Injectable, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { GraphqlClient } from '../../../core/graphql/graphql.client';
import type {
  ConnectZohoMutation,
  DisconnectZohoMutation,
  ZohoConnectionQuery,
} from '../../../graphql/generated';
import { ConnectZohoDocument, DisconnectZohoDocument, ZohoConnectionDocument } from './zoho-connection.operations';

/** Result of returning from Zoho's consent page (?zoho=connected|failed). */
export type ConnectOutcome = 'connected' | 'failed' | null;

/**
 * "Connect Zoho People": the employee grants OneSpace read access once; the API keeps the refresh token
 * encrypted and Zoho enforces the employee's own permissions (FR-2).
 */
@Injectable()
export class ZohoConnectionStore {
  private readonly graphql = inject(GraphqlClient);
  private readonly document = inject(DOCUMENT);
  private readonly connectedState = signal<boolean | null>(null);
  private readonly busyState = signal(false);

  /** null until loaded. */
  readonly connected = this.connectedState.asReadonly();
  readonly busy = this.busyState.asReadonly();

  async load(): Promise<void> {
    try {
      const data = await firstValueFrom(this.graphql.request<ZohoConnectionQuery>(ZohoConnectionDocument));
      this.connectedState.set(data.zohoConnection.connected);
    } catch {
      this.connectedState.set(null);
    }
  }

  /** Sends the browser to Zoho's consent page; Zoho returns it to the assistant with ?zoho=… */
  async connect(): Promise<void> {
    if (this.busyState()) return;
    this.busyState.set(true);
    try {
      const data = await firstValueFrom(this.graphql.request<ConnectZohoMutation>(ConnectZohoDocument));
      this.document.location.assign(data.connectZoho.authorizationUrl);
    } catch {
      this.busyState.set(false);
      throw new Error('Could not start the Zoho People connection. Try again in a moment.');
    }
  }

  async disconnect(): Promise<void> {
    this.busyState.set(true);
    try {
      await firstValueFrom(this.graphql.request<DisconnectZohoMutation>(DisconnectZohoDocument));
      this.connectedState.set(false);
    } finally {
      this.busyState.set(false);
    }
  }
}

export function parseConnectOutcome(value: string | null | undefined): ConnectOutcome {
  return value === 'connected' || value === 'failed' ? value : null;
}
