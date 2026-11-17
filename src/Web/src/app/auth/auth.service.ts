import { computed, inject, Injectable, signal } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { Router } from '@angular/router';
import { AuthConfig, OAuthService } from 'angular-oauth2-oidc';
import { permissionsForRoles } from './permissions';

export interface RailwayUser {
  username: string;
  name: string;
  email: string;
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly oauth = inject(OAuthService);
  private readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);

  readonly isLoggedIn = signal(false);
  readonly user = signal<RailwayUser | null>(null);
  readonly roles = signal<readonly string[]>([]);
  readonly permissions = computed(() => permissionsForRoles(this.roles()));
  readonly authError = signal<string | null>(null);

  accessToken(): string | null {
    return this.isLoggedIn() ? this.oauth.getAccessToken() : null;
  }

  constructor() {
    this.oauth.events.subscribe((event) => {
      if (
        ['token_received', 'token_refreshed', 'logout', 'session_terminated'].includes(event.type)
      ) {
        this.updateSession();
      }
    });
  }

  async initialize(): Promise<void> {
    const origin = this.document.location.origin;
    const config: AuthConfig = {
      issuer: 'http://localhost:8180/',
      clientId: 'web',
      responseType: 'code',
      disablePKCE: false,
      scope: 'openid profile email roles offline_access',
      redirectUri: new URL('/callback', origin).toString(),
      postLogoutRedirectUri: new URL('/', origin).toString(),
      requireHttps: false,
      clearHashAfterLogin: true,
    };

    this.oauth.configure(config);
    try {
      await this.oauth.loadDiscoveryDocumentAndTryLogin();
      this.oauth.setupAutomaticSilentRefresh();
      this.updateSession();

      this.authError.set(null);
    } catch {
      this.authError.set('Sign-in is unavailable. Try again shortly.');
    }
  }

  returnUrl(): string {
    try {
      // angular-oauth2-oidc URI-encodes custom state when constructing the login URL.
      return this.safeInternalUrl(decodeURIComponent(this.oauth.state || '/'));
    } catch {
      return '/';
    }
  }

  login(returnUrl = this.router.url): void {
    this.oauth.initCodeFlow(this.safeInternalUrl(returnUrl));
  }

  logout(): void {
    this.isLoggedIn.set(false);
    this.user.set(null);
    this.roles.set([]);
    this.oauth.logOut();
  }

  private updateSession(): void {
    const signedIn = this.oauth.hasValidAccessToken();
    const claims = this.oauth.getIdentityClaims();

    this.isLoggedIn.set(signedIn);
    if (!signedIn || !claims) {
      this.user.set(null);
      this.roles.set([]);
      return;
    }

    const roleClaims = claims['role'];
    const roles =
      typeof roleClaims === 'string'
        ? [roleClaims]
        : Array.isArray(roleClaims)
          ? roleClaims.filter((role): role is string => typeof role === 'string')
          : [];
    const username = this.stringClaim(claims, 'preferred_username');

    this.user.set({
      username,
      name: this.stringClaim(claims, 'name') || username,
      email: this.stringClaim(claims, 'email'),
    });
    this.roles.set(roles);
  }

  private stringClaim(claims: Record<string, unknown>, key: string): string {
    const value = claims[key];
    return typeof value === 'string' ? value : '';
  }

  private safeInternalUrl(value: string): string {
    if (!value.startsWith('/') || value.startsWith('//') || value.startsWith('/callback')) {
      return '/';
    }

    try {
      const url = new URL(value, this.document.location.origin);
      if (url.origin !== this.document.location.origin) {
        return '/';
      }
      return `${url.pathname}${url.search}${url.hash}`;
    } catch {
      return '/';
    }
  }
}
