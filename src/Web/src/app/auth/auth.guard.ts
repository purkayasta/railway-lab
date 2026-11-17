import { inject } from '@angular/core';
import { CanActivateFn, Router } from '@angular/router';
import { AuthService } from './auth.service';

export const authGuard: CanActivateFn = (_route, state) => {
  const auth = inject(AuthService);
  if (auth.isLoggedIn()) return true;

  auth.login(state.url);
  return false;
};

export function permissionGuard(permission: string): CanActivateFn {
  return (_route, state) => {
    const auth = inject(AuthService);
    if (!auth.isLoggedIn()) {
      auth.login(state.url);
      return false;
    }

    return auth.permissions().has(permission) ? true : inject(Router).parseUrl('/forbidden');
  };
}
