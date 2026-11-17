import { Component, DestroyRef, inject, signal } from '@angular/core';
import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';

interface Hold {
  holdGroupPublicId: string;
  tripPublicId: string;
  expiresAtUtc: string;
  seatPublicIds: string[];
  origin: string;
  destination: string;
  totalAmount: number;
}
interface Booking {
  publicId: string;
  status: string;
  totalAmount: number;
}
interface Layout {
  coaches: { label: string; seats: { publicId: string; label: string }[] }[];
}

@Component({
  selector: 'app-checkout-page',
  imports: [DatePipe, DecimalPipe, RouterLink],
  template: `
    <section class="flow-page checkout">
      <a class="back-link" routerLink="/">Back to search</a>
      <ol class="journey-steps" aria-label="Booking steps">
        <li>Find a train</li>
        <li>Choose seats</li>
        <li aria-current="step">Confirm booking</li>
      </ol>
      <h1>Review your seats</h1>
      <p class="page-description">Check your journey and confirm before your hold expires.</p>
      @if (error()) {
        <p class="message" role="alert">{{ error() }}</p>
      }
      @if (!hold() && !error()) {
        <p class="message" role="status">Loading your reservation…</p>
      }
      @if (hold(); as quote) {
        <div class="checkout-layout">
          <section class="panel" aria-label="Journey summary">
            <h2>{{ quote.origin }} → {{ quote.destination }}</h2>
            <dl class="summary-list">
              <div>
                <dt>Seats</dt>
                <dd>{{ seatLabels() || quote.seatPublicIds.length + ' reserved seats' }}</dd>
              </div>
              <div>
                <dt>Total demo fare</dt>
                <dd class="summary-total">{{ quote.totalAmount | number: '1.2-2' }}</dd>
              </div>
              <div>
                <dt>Held until</dt>
                <dd>{{ quote.expiresAtUtc | date: 'shortTime' }}</dd>
              </div>
            </dl>
            <p class="reference">
              {{ quote.expiresAtUtc | date: 'mediumDate' }} · Times shown in your local timezone
            </p>
          </section>
          @if (!booking()) {
            <section class="confirmation-actions" aria-label="Confirm your booking">
              @if (expired()) {
                <p class="message" role="alert">
                  Your hold has expired. Return to search to choose seats again.
                </p>
              } @else {
                <p class="hold-countdown">
                  <span class="countdown-value" role="timer" aria-label="Time left on hold"
                    >{{ remaining() }} remaining</span
                  ><span>Your seats are temporarily held</span>
                </p>
              }
              <div class="button-row">
                <button
                  class="secondary-button"
                  type="button"
                  [disabled]="busy() || extended() || expired()"
                  (click)="extend()"
                >
                  {{ extended() ? 'Hold extended' : 'Extend hold' }}</button
                ><button class="text-button" type="button" [disabled]="busy()" (click)="release()">
                  Release seats
                </button>
              </div>
              <p class="demo-note">This demo simulates payment; no money will be charged.</p>
              <button
                class="primary-button"
                type="button"
                [disabled]="busy() || expired()"
                (click)="confirm()"
              >
                {{ busy() ? 'Confirming…' : 'Confirm booking' }}
              </button>
            </section>
          }
        </div>
      }
      @if (booking(); as result) {
        <section class="confirmation-result message" role="status">
          @if (result.status === 'Confirmed') {
            <h2>Booking confirmed</h2>
            <p>Payment was simulated. No money was charged.</p>
          } @else if (result.status === 'PaymentOrphaned') {
            <h2>Booking needs review</h2>
            <p>The seats could not be confirmed. No money was charged.</p>
          } @else {
            <h2>Booking pending payment</h2>
            <p>Payment has not been taken. Your booking is not yet confirmed.</p>
          }
          <p class="reference">Reference: {{ result.publicId }}</p>
          <a class="secondary-button" routerLink="/my-bookings">View my bookings</a>
        </section>
      }
    </section>
  `,
  styles: `
    .checkout {
      max-width: 64rem;
    }
    .checkout-layout {
      display: grid;
      grid-template-columns: 1.1fr 1fr;
      gap: 2.5rem;
      align-items: start;
    }
    .hold-countdown {
      display: grid;
      gap: 0.4rem;
      margin: 0;
    }
    .countdown-value {
      font-size: 1.5rem;
      font-weight: 600;
      font-variant-numeric: tabular-nums;
    }
    .hold-countdown > span:last-child,
    .demo-note {
      font-size: 0.85rem;
      color: var(--color-muted);
    }
    .demo-note {
      margin-top: 2rem;
    }
    .confirmation-actions > .primary-button {
      width: 100%;
    }
    .confirmation-result {
      margin-top: 2rem;
    }
    .confirmation-result p {
      margin-bottom: 1rem;
    }
    @media (max-width: 750px) {
      .checkout-layout {
        grid-template-columns: 1fr;
        gap: 1.5rem;
      }
    }
  `,
})
export class CheckoutPage {
  private readonly route = inject(ActivatedRoute);
  private readonly http = inject(HttpClient);
  private readonly router = inject(Router);
  private readonly destroyRef = inject(DestroyRef);
  readonly groupId = this.route.snapshot.paramMap.get('id') ?? '';
  readonly hold = signal<Hold | null>(null);
  readonly booking = signal<Booking | null>(null);
  readonly seatLabels = signal('');
  readonly busy = signal(false);
  readonly error = signal('');
  readonly now = signal(Date.now());
  readonly extended = signal(false);

  constructor() {
    const timer = setInterval(() => this.now.set(Date.now()), 1000);
    this.destroyRef.onDestroy(() => clearInterval(timer));
    this.http.get<Hold>(`/api/reservation/holds/${this.groupId}`).subscribe({
      next: (hold) => {
        this.hold.set(hold);
        this.http.get<Layout>(`/api/schedule/trips/${hold.tripPublicId}/layout`).subscribe({
          next: (layout) =>
            this.seatLabels.set(
              layout.coaches
                .flatMap((coach) =>
                  coach.seats
                    .filter((seat) => hold.seatPublicIds.includes(seat.publicId))
                    .map((seat) => `${coach.label} ${seat.label}`),
                )
                .join(', '),
            ),
          error: () => this.seatLabels.set(''),
        });
      },
      error: () =>
        this.error.set('This hold is unavailable or has expired. Please select your seats again.'),
    });
  }

  expired(): boolean {
    const quote = this.hold();
    return quote === null || Date.parse(quote.expiresAtUtc) <= this.now();
  }

  remaining(): string {
    const quote = this.hold();
    const seconds = quote
      ? Math.max(0, Math.ceil((Date.parse(quote.expiresAtUtc) - this.now()) / 1000))
      : 0;
    return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
  }

  extend(): void {
    this.busy.set(true);
    this.error.set('');
    this.http.post<Hold>(`/api/reservation/holds/${this.groupId}/extend`, {}).subscribe({
      next: (hold) => {
        this.hold.set(hold);
        this.extended.set(true);
        this.busy.set(false);
      },
      error: () => {
        this.busy.set(false);
        this.error.set(
          'The hold could not be extended. It may have expired or already been extended.',
        );
      },
    });
  }

  release(): void {
    this.busy.set(true);
    this.error.set('');
    this.http.delete(`/api/reservation/holds/${this.groupId}`).subscribe({
      next: () => {
        void this.router.navigateByUrl('/');
      },
      error: () => {
        this.busy.set(false);
        this.error.set('The seats could not be released. Please try again.');
      },
    });
  }

  confirm(): void {
    this.busy.set(true);
    this.error.set('');
    this.http.post<Booking>(`/api/reservation/holds/${this.groupId}/checkout`, {}).subscribe({
      next: (booking) => {
        this.booking.set(booking);
        this.busy.set(false);
      },
      error: () => {
        this.busy.set(false);
        this.error.set('The booking could not be started. Your hold may have expired.');
      },
    });
  }
}
