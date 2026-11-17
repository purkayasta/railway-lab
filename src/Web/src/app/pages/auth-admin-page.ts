import { Component } from '@angular/core';

@Component({
  selector: 'app-auth-admin-page',
  template: `
    <section class="access-page">
      <h1>Roles &amp; permissions</h1>
      <p>
        Role editing and the permission audit trail are planned. This page does not yet provide
        access management controls.
      </p>
      <div class="access-summary">
        <span class="access-summary__count">4</span>
        <span>seeded roles in the current access model</span>
      </div>
    </section>
  `,
  styles: `
    .access-page {
      max-width: 70rem;
      margin: 0 auto;
      padding: 3rem 0;
    }
    h1 {
      margin: 0 0 0.8rem;
      color: var(--color-ink);
      font-family: var(--font-display);
      font-size: 2.75rem;
      letter-spacing: -0.025em;
    }
    p {
      max-width: 40rem;
      margin: 0;
      color: var(--color-muted);
      line-height: 1.65;
    }
    .access-summary {
      display: flex;
      align-items: center;
      gap: 1rem;
      max-width: 20rem;
      margin-top: 2rem;
      padding: 1.25rem;
      border: 1px solid var(--color-border);
      border-radius: 0.85rem;
      background: var(--color-surface);
      color: var(--color-muted);
      font-size: 0.85rem;
    }
    .access-summary__count {
      color: var(--color-accent);
      font-family: var(--font-display);
      font-size: 2rem;
      font-weight: 700;
    }
  `,
})
export class AuthAdminPage {}
