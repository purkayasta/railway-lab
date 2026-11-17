import { DOCUMENT } from '@angular/common';
import { HttpErrorResponse, HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { Router } from '@angular/router';
import { catchError, throwError } from 'rxjs';
import { AuthService } from './auth.service';
import { ToastService } from '../toast.service';

export function isSameOriginApiUrl(url: string, origin: string): boolean {
  try {
    const requestUrl = new URL(url, origin);
    return requestUrl.origin === origin && requestUrl.pathname.startsWith('/api/');
  } catch {
    return false;
  }
}

function problemMessage(error: HttpErrorResponse): string {
  const body: unknown = error.error;
  if (typeof body === 'object' && body !== null) {
    const detail = (body as Record<string, unknown>)['detail'];
    const title = (body as Record<string, unknown>)['title'];
    if (typeof detail === 'string' && detail.trim()) return detail;
    if (typeof title === 'string' && title.trim()) return title;
  }
  if (error.status === 0) return 'Connection lost. Check your connection and try again.';
  if (error.status === 429) return 'Too many requests. Please wait a moment and try again.';
  if (error.status >= 500)
    return 'The service is temporarily unavailable. Please try again shortly.';
  return 'The request could not be completed. Please try again.';
}

export const apiAuthInterceptor: HttpInterceptorFn = (request, next) => {
  const auth = inject(AuthService);
  const router = inject(Router);
  const toast = inject(ToastService);
  const origin = inject(DOCUMENT).location.origin;

  const apiRequest = isSameOriginApiUrl(request.url, origin);
  const accessToken = apiRequest && auth.isLoggedIn() ? auth.accessToken() : null;
  const authorizedRequest =
    accessToken === null
      ? request
      : request.clone({ setHeaders: { Authorization: `Bearer ${accessToken}` } });

  return next(authorizedRequest).pipe(
    catchError((error: unknown) => {
      if (apiRequest && error instanceof HttpErrorResponse) {
        toast.show(problemMessage(error));
        if (error.status === 401 && !auth.isLoggedIn()) {
          auth.login(router.url);
        }
      }
      return throwError(() => error);
    }),
  );
};
