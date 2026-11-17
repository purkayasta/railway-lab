import { Component, inject } from '@angular/core';
import { Router, RouterLink } from '@angular/router';
import { AuthService } from '../auth/auth.service';

const STATUS_STYLES = `
  .status-page { max-width: 45rem; margin: 0 auto; padding: clamp(4rem, 12vw, 8rem) 0; }
  h1 { margin: 0 0 1rem; color: var(--color-ink); font-family: var(--font-display); font-size: 2.75rem; letter-spacing: -.025em; }
  p { max-width: 34rem; margin: 0 0 1.5rem; color: var(--color-muted); font-size: 1rem; line-height: 1.7; }
  a { color: var(--color-accent); font-size: .9rem; font-weight: 700; text-decoration-thickness: 1px; text-underline-offset: .2em; }
  .status-tag { display: inline-block; padding: .45rem .65rem; border: 1px solid var(--color-border); border-radius: .25rem; color: var(--color-muted); font-size: .75rem; }
`;

@Component({
  selector: 'app-helpdesk-page',
  template: `
    <section class="status-page">
      <h1>How can we help?</h1>
      <p>
        Booking support is planned and is not available yet. You can review existing reservations in
        My bookings.
      </p>
      <span class="status-tag">Sign-in required</span>
    </section>
  `,
  styles: [STATUS_STYLES],
})
export class HelpdeskPage {}

@Component({
  selector: 'app-forbidden-page',
  imports: [RouterLink],
  template: `
    <section class="status-page">
      <h1>This area is restricted.</h1>
      <p>Your account does not have the permission required for this page.</p>
      <a routerLink="/">Back to search</a>
    </section>
  `,
  styles: [STATUS_STYLES],
})
export class ForbiddenPage {}

@Component({
  selector: 'app-callback-page',
  imports: [RouterLink],
  template: `
    <section class="status-page" role="status">
      @if (auth.isLoggedIn()) {
        <h1>Completing sign-in…</h1>
        <p>Returning you to your trip.</p>
      } @else {
        <h1>Sign-in could not be completed.</h1>
        <p>Please return to search and try signing in again.</p>
        <a routerLink="/">Return to search</a>
      }
    </section>
  `,
  styles: [STATUS_STYLES],
})
export class CallbackPage {
  protected readonly auth = inject(AuthService);

  constructor() {
    if (this.auth.isLoggedIn()) {
      void inject(Router).navigateByUrl(this.auth.returnUrl(), { replaceUrl: true });
    }
  }
}

@Component({
  selector: 'app-not-found-page',
  imports: [RouterLink],
  template: `
    <section class="status-page">
      <h1>This stop isn’t on the map.</h1>
      <p>That address doesn’t match a RailwayLab page.</p>
      <a routerLink="/">Return to search</a>
    </section>
  `,
  styles: [STATUS_STYLES],
})
export class NotFoundPage {}
