import { signal } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { provideRouter, Router } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { CallbackPage } from './status-pages';

describe('sign-in callback', () => {
  it('returns a signed-in user to the requested page', () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        {
          provide: AuthService,
          useValue: {
            isLoggedIn: signal(true),
            returnUrl: () => '/my-bookings',
          },
        },
      ],
    });
    const navigate = vi.spyOn(TestBed.inject(Router), 'navigateByUrl').mockResolvedValue(true);

    TestBed.createComponent(CallbackPage);

    expect(navigate).toHaveBeenCalledWith('/my-bookings', { replaceUrl: true });
  });

  it('offers a way back when sign-in did not complete', () => {
    TestBed.configureTestingModule({
      providers: [
        provideRouter([]),
        { provide: AuthService, useValue: { isLoggedIn: signal(false) } },
      ],
    });
    const page = TestBed.createComponent(CallbackPage);
    page.detectChanges();

    expect(page.nativeElement.textContent).toContain('Sign-in could not be completed');
  });
});
