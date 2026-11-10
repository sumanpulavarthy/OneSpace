import { DOCUMENT } from '@angular/common';
import { Injectable, computed, inject, signal } from '@angular/core';
import type { AccountInfo, IPublicClientApplication } from '@azure/msal-browser';
import { RuntimeConfigService } from '../config/runtime-config.service';

const SIGN_IN_SCOPES = ['openid', 'profile', 'email'];

export interface SignedInUser {
  readonly name: string;
  readonly email: string;
}

/**
 * Entra ID sign-in (OIDC, redirect flow) — same approach as cmart-accord. MSAL loads only when first needed.
 * When no Entra ID config is supplied the app runs in local dev mode and the API's development user is used.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly runtime = inject(RuntimeConfigService);
  private readonly document = inject(DOCUMENT);
  private readonly currentUser = signal<SignedInUser | null>(null);
  private client?: Promise<IPublicClientApplication>;

  /** False in local dev mode (no Entra ID config): requests are sent without a token. */
  readonly configured = computed(() => this.runtime.azureAd() !== null);
  readonly user = this.currentUser.asReadonly();

  async signIn(): Promise<void> {
    const client = await this.getClient();
    // Request the API scope at sign-in so later silent token requests succeed without another prompt.
    const scopes = [...SIGN_IN_SCOPES, ...(this.runtime.azureAd()?.apiScopes ?? [])];
    await client.loginRedirect({ scopes, prompt: 'select_account' });
  }

  /** True when a user is signed in and a token can be obtained. Also completes a returning redirect. */
  async hasValidSession(): Promise<boolean> {
    if (!this.configured()) return false;
    try {
      const client = await this.getClient();
      const result = await client.handleRedirectPromise();
      const account = result?.account ?? client.getActiveAccount() ?? client.getAllAccounts()[0] ?? null;
      if (!account) return false;
      client.setActiveAccount(account);
      this.currentUser.set(toSignedInUser(account));
      return (await this.getAccessToken()) !== null;
    } catch {
      return false;
    }
  }

  /** The signed-in user's API access token, or null in dev mode / when nobody is signed in. */
  async getAccessToken(): Promise<string | null> {
    const azureAd = this.runtime.azureAd();
    if (!azureAd) return null;

    const client = await this.getClient();
    const account = client.getActiveAccount() ?? client.getAllAccounts()[0];
    if (!account) return null;

    const scopes = azureAd.apiScopes.length > 0 ? [...azureAd.apiScopes] : SIGN_IN_SCOPES;
    const result = await client.acquireTokenSilent({ account, scopes });
    return result.accessToken;
  }

  async signOut(): Promise<void> {
    if (!this.configured()) return;
    const client = await this.getClient();
    this.currentUser.set(null);
    await client.logoutRedirect({ account: client.getActiveAccount() ?? undefined });
  }

  private getClient(): Promise<IPublicClientApplication> {
    this.client ??= this.createClient();
    return this.client;
  }

  private async createClient(): Promise<IPublicClientApplication> {
    const azureAd = this.runtime.azureAd();
    if (!azureAd) throw new Error('Microsoft sign-in is not configured.');

    const origin = this.document.location.origin;
    const { createStandardPublicClientApplication } = await import('@azure/msal-browser');
    return createStandardPublicClientApplication({
      auth: {
        clientId: azureAd.clientId,
        authority: `https://login.microsoftonline.com/${azureAd.tenantId}`,
        redirectUri: `${origin}/assistant`,
        postLogoutRedirectUri: `${origin}/login`,
      },
      // Session storage: tokens do not outlive the browser session (NFR).
      cache: { cacheLocation: 'sessionStorage' },
    });
  }
}

function toSignedInUser(account: AccountInfo): SignedInUser {
  return { name: account.name?.trim() || account.username, email: account.username };
}
