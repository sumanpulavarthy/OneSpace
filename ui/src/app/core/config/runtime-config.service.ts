import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';

/** Public Entra ID app registration values. Never put a client secret here. */
export interface AzureAdConfig {
  readonly clientId: string;
  readonly tenantId: string;
  /** Scopes the OneSpace API exposes, e.g. api://<api-client-id>/access_as_user. */
  readonly apiScopes: readonly string[];
}

export interface MenuItem {
  readonly label: string;
  readonly route: string;
}

/**
 * Runtime configuration loaded at bootstrap from assets/config/app-config.json, so one build can be
 * promoted across environments (the file is replaced per environment at deploy time).
 */
@Injectable({ providedIn: 'root' })
export class RuntimeConfigService {
  private readonly document = inject(DOCUMENT);
  private readonly apiBase = signal('');
  private readonly azureAdConfig = signal<AzureAdConfig | null>(null);
  private readonly menuItems = signal<readonly MenuItem[]>([]);

  /** API origin; empty means same origin (the dev server proxies /graphql to the API). */
  readonly apiBaseUrl = computed(() => this.apiBase().trim().replace(/\/$/, '') || this.document.location.origin);
  readonly azureAd = this.azureAdConfig.asReadonly();
  readonly menu = this.menuItems.asReadonly();

  async load(): Promise<void> {
    const [config, menu] = await Promise.all([
      this.fetchJson('assets/config/app-config.json'),
      this.fetchJson('assets/config/menu.json'),
    ]);

    if (isRecord(config)) {
      if (typeof config['apiBaseUrl'] === 'string') this.apiBase.set(config['apiBaseUrl']);
      this.azureAdConfig.set(parseAzureAd(config['azureAd']));
    }

    if (Array.isArray(menu)) {
      this.menuItems.set(
        menu.filter(
          (m): m is MenuItem => isRecord(m) && typeof m['label'] === 'string' && typeof m['route'] === 'string',
        ),
      );
    }
  }

  private async fetchJson(path: string): Promise<unknown> {
    try {
      const response = await fetch(new URL(path, this.document.baseURI), { cache: 'no-store' });
      return response.ok ? await response.json() : undefined;
    } catch {
      // Missing config keeps the safe defaults (same-origin API, dev-mode sign-in).
      return undefined;
    }
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

export function parseAzureAd(value: unknown): AzureAdConfig | null {
  if (!isRecord(value)) return null;
  const clientId = typeof value['clientId'] === 'string' ? value['clientId'].trim() : '';
  const tenantId = typeof value['tenantId'] === 'string' ? value['tenantId'].trim() : '';
  const apiScopes = Array.isArray(value['apiScopes'])
    ? value['apiScopes'].filter((s): s is string => typeof s === 'string' && s.trim() !== '').map(s => s.trim())
    : [];
  return clientId && tenantId ? { clientId, tenantId, apiScopes } : null;
}
