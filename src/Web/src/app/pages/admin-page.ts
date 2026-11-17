import { Component } from '@angular/core';

@Component({
  selector: 'app-admin-page',
  template: `
    <section class="admin-page">
      <h1>Service dashboard</h1>
      <p>Sales and occupancy reports are planned. No live reporting data is available yet.</p>
      <div class="dashboard-state">
        <div>
          <strong>Analytics service not connected</strong>
          <p>
            This protected dashboard will show sales, occupancy, and payment health once projections
            are available.
          </p>
        </div>
      </div>
    </section>
  `,
  styles: `
    .admin-page {
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
    .dashboard-state {
      display: flex;
      gap: 1rem;
      max-width: 42rem;
      margin-top: 2rem;
      padding: 1.4rem;
      border: 1px solid var(--color-border);
      border-radius: 0.85rem;
      background: var(--color-surface);
    }
    strong {
      color: var(--color-ink);
    }
    .dashboard-state p {
      margin-top: 0.5rem;
      font-size: 0.85rem;
    }
  `,
})
export class AdminPage {}
