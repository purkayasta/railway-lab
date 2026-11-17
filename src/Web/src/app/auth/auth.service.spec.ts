import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { OAuthEvent, OAuthService, OAuthSuccessEvent } from 'angular-oauth2-oidc';
import { Subject } from 'rxjs';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  let events: Subject<OAuthEvent>;
  const claims: Record<string, unknown> = {
    preferred_username: 'alice',
    name: 'Alice Passenger',
    email: 'alice@railway.test',
    role: ['passenger'],
  };
  let validAccessToken: boolean;
  let oauth: ReturnType<typeof createOAuthStub>;

  function createOAuthStub() {
    return {
      events: events.asObservable(),
      configure: vi.fn(),
      loadDiscoveryDocumentAndTryLogin: vi.fn().mockResolvedValue(true),
      hasValidAccessToken: vi.fn(() => validAccessToken),
      getIdentityClaims: vi.fn(() => claims),
      setupAutomaticSilentRefresh: vi.fn(),
      initCodeFlow: vi.fn(),
      logOut: vi.fn(),
      state: '',
    };
  }

  beforeEach(() => {
    events = new Subject<OAuthEvent>();
    validAccessToken = true;
    oauth = createOAuthStub();
    TestBed.configureTestingModule({
      providers: [provideRouter([]), AuthService, { provide: OAuthService, useValue: oauth }],
    });
  });

  afterEach(() => events.complete());

  it('initializes PKCE and exposes the signed-in user and permissions', async () => {
    const auth = TestBed.inject(AuthService);

    await auth.initialize();

    expect(oauth.configure).toHaveBeenCalledWith(
      expect.objectContaining({
        issuer: 'http://localhost:8180/',
        scope: 'openid profile email roles offline_access',
        clientId: 'web',
        responseType: 'code',
        disablePKCE: false,
        redirectUri: expect.stringContaining('/callback'),
      }),
    );
    expect(auth.isLoggedIn()).toBe(true);
    expect(auth.user()?.username).toBe('alice');
    expect(auth.roles()).toEqual(['passenger']);
    expect(auth.permissions().has('seats:hold')).toBe(true);
    expect(oauth.setupAutomaticSilentRefresh).toHaveBeenCalledOnce();
  });

  it('updates the signed-in state when the token is refreshed', async () => {
    const auth = TestBed.inject(AuthService);
    await auth.initialize();

    validAccessToken = false;
    events.next(new OAuthSuccessEvent('token_refreshed'));

    expect(auth.isLoggedIn()).toBe(false);
    expect(auth.roles()).toEqual([]);
  });

  it('only carries a same-app route through login', () => {
    const auth = TestBed.inject(AuthService);

    auth.login('//example.com/steal');

    expect(oauth.initCodeFlow).toHaveBeenCalledWith('/');
  });

  it('restores the encoded trip route after PKCE login', () => {
    oauth.state = encodeURIComponent('/trips/trip-1?from=DHA&to=CTG&class=economy');
    expect(TestBed.inject(AuthService).returnUrl()).toBe(
      '/trips/trip-1?from=DHA&to=CTG&class=economy',
    );
  });

  it.each(['%2F%2Foutside.example', '%2Fcallback', '%E0%A4%A'])(
    'rejects unsafe or malformed return state %s',
    (state) => {
      oauth.state = state;
      expect(TestBed.inject(AuthService).returnUrl()).toBe('/');
    },
  );

  it('keeps the app usable and exposes an error when Auth is unavailable', async () => {
    oauth.loadDiscoveryDocumentAndTryLogin.mockRejectedValue(new Error('offline'));
    const auth = TestBed.inject(AuthService);

    await auth.initialize();

    expect(auth.authError()).toBe('Sign-in is unavailable. Try again shortly.');
  });

  it('ends the local session before redirecting to Auth logout', async () => {
    const auth = TestBed.inject(AuthService);
    await auth.initialize();

    auth.logout();

    expect(auth.isLoggedIn()).toBe(false);
    expect(auth.roles()).toEqual([]);
    expect(oauth.logOut).toHaveBeenCalledOnce();
  });
});
