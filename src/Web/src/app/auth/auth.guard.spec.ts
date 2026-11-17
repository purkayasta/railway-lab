import { TestBed } from '@angular/core/testing';
import { ActivatedRouteSnapshot, provideRouter, Router, RouterStateSnapshot, UrlTree } from '@angular/router';
import { signal } from '@angular/core';
import { AuthService } from './auth.service';
import { authGuard, permissionGuard } from './auth.guard';

describe('authentication guards', () => {
  let signedIn: ReturnType<typeof signal<boolean>>;
  let grantedPermissions: ReturnType<typeof signal<ReadonlySet<string>>>;
  let auth: { isLoggedIn: typeof signedIn; permissions: typeof grantedPermissions; login: ReturnType<typeof vi.fn> };

  beforeEach(() => {
    signedIn = signal(false);
    grantedPermissions = signal<ReadonlySet<string>>(new Set());
    auth = { isLoggedIn: signedIn, permissions: grantedPermissions, login: vi.fn() };
    TestBed.configureTestingModule({
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    });
  });

  const activate = (guard: typeof authGuard | ReturnType<typeof permissionGuard>) =>
    TestBed.runInInjectionContext(() =>
      guard(new ActivatedRouteSnapshot(), { url: '/admin' } as RouterStateSnapshot),
    );

  it('starts login and preserves the requested route for a logged-out visitor', () => {
    expect(activate(authGuard)).toBe(false);
    expect(auth.login).toHaveBeenCalledWith('/admin');
  });

  it('redirects signed-in users without a permission to the forbidden page', () => {
    signedIn.set(true);

    const result = activate(permissionGuard('reports:read'));

    expect(result).toBeInstanceOf(UrlTree);
    if (result instanceof UrlTree) {
      expect(result.toString()).toBe('/forbidden');
    }
  });

  it('allows a user who has the required permission', () => {
    signedIn.set(true);
    grantedPermissions.set(new Set(['reports:read']));

    expect(activate(permissionGuard('reports:read'))).toBe(true);
  });
});
