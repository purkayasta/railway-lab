import { HttpClient, provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { AuthService } from './auth.service';
import { apiAuthInterceptor } from './api.interceptor';
import { ToastService } from '../toast.service';

describe('apiAuthInterceptor', () => {
  const auth = {
    isLoggedIn: signal(true),
    accessToken: () => 'access-token',
    login: vi.fn(),
  };

  beforeEach(() => {
    auth.isLoggedIn.set(true);
    auth.login.mockClear();
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(withInterceptors([apiAuthInterceptor])),
        provideHttpClientTesting(),
        { provide: AuthService, useValue: auth },
      ],
    });
  });

  afterEach(() => {
    TestBed.inject(HttpTestingController).verify();
  });

  it('attaches the access token to same-origin API requests only', () => {
    const http = TestBed.inject(HttpClient);
    const requests = TestBed.inject(HttpTestingController);

    http.get('/api/schedule/trips').subscribe();
    http.get('/assets/status.json').subscribe();
    http.get('https://outside.example/api/data').subscribe();

    expect(requests.expectOne('/api/schedule/trips').request.headers.get('Authorization')).toBe(
      'Bearer access-token',
    );
    expect(requests.expectOne('/assets/status.json').request.headers.has('Authorization')).toBe(
      false,
    );
    expect(
      requests.expectOne('https://outside.example/api/data').request.headers.has('Authorization'),
    ).toBe(false);
  });

  it('shows readable ProblemDetails for an API error', () => {
    const http = TestBed.inject(HttpClient);
    const requests = TestBed.inject(HttpTestingController);
    const toast = TestBed.inject(ToastService);

    http.get('/api/analytics/reports').subscribe({ error: () => undefined });
    requests
      .expectOne('/api/analytics/reports')
      .flush(
        { title: 'Forbidden', detail: 'You do not have report access.' },
        { status: 403, statusText: 'Forbidden' },
      );

    expect(toast.message()).toBe('You do not have report access.');
  });

  it('starts login after an unauthenticated API request is rejected', () => {
    const http = TestBed.inject(HttpClient);
    const requests = TestBed.inject(HttpTestingController);
    auth.isLoggedIn.set(false);

    http.get('/api/reservation/bookings').subscribe({ error: () => undefined });
    requests
      .expectOne('/api/reservation/bookings')
      .flush({ title: 'Unauthorized' }, { status: 401, statusText: 'Unauthorized' });

    expect(auth.login).toHaveBeenCalledWith('/');
  });

  it('shows recovery guidance instead of the raw HTTP URL for a service failure', () => {
    TestBed.inject(HttpClient)
      .get('/api/schedule/stations')
      .subscribe({ error: () => undefined });
    TestBed.inject(HttpTestingController)
      .expectOne('/api/schedule/stations')
      .flush('upstream unavailable', { status: 502, statusText: 'Bad Gateway' });
    expect(TestBed.inject(ToastService).message()).toBe(
      'The service is temporarily unavailable. Please try again shortly.',
    );
  });
});
