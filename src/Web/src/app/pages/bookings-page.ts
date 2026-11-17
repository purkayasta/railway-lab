import { DatePipe, DecimalPipe } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Component, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';

interface Booking {
  publicId: string;
  origin: string;
  destination: string;
  status: string;
  totalAmount: number;
  createdAtUtc: string;
  passengerName: string | null;
}

@Component({
  selector: 'app-bookings-page',
  imports: [DatePipe, DecimalPipe, RouterLink],
  template: `
    <section class="flow-page bookings">
      <div class="bookings-heading">
        <div>
          <h1>My bookings</h1>
          <p class="page-description">
            Your journeys, booking references and confirmation details.
          </p>
        </div>
        <a class="secondary-button" routerLink="/">Find another train</a>
      </div>
      @if (loading()) {
        <p class="message" role="status">Loading your bookings…</p>
      } @else if (error()) {
        <p class="message" role="alert">{{ error() }}</p>
        <button class="secondary-button" type="button" (click)="load()">Try again</button>
      } @else if (bookings().length === 0) {
        <div class="empty-state">
          <h2>Your first journey is ahead</h2>
          <p>You have no bookings yet. Find a train and choose your seats to get started.</p>
          <a class="primary-button" routerLink="/">Search for a trip</a>
        </div>
      } @else {
        <div class="booking-list">
          @for (booking of bookings(); track booking.publicId) {
            <article class="booking-row">
              <div class="booking-route">
                <h2>{{ booking.origin }} → {{ booking.destination }}</h2>
                <span class="status-badge" [attr.data-status]="booking.status">{{
                  statusLabel(booking.status)
                }}</span>
                @if (booking.passengerName) {
                  <p>Passenger: {{ booking.passengerName }}</p>
                }
                @if (booking.status === 'PendingPayment') {
                  <p>Payment has not been taken. This booking is not yet confirmed.</p>
                }
                @if (booking.status === 'Confirmed') {
                  <p>Payment was simulated. No money was charged.</p>
                }
                @if (booking.status === 'Expired') {
                  <p>The hold expired before confirmation. Search again to reserve seats.</p>
                }
                @if (booking.status === 'PaymentOrphaned') {
                  <p>The seats could not be confirmed. No money was charged.</p>
                }
                <p class="reference">Reference: {{ booking.publicId }}</p>
              </div>
              <div class="booking-meta">
                <strong>{{ booking.totalAmount | number: '1.2-2' }}</strong
                ><span>Total demo fare</span
                ><span>Booked {{ booking.createdAtUtc | date: 'mediumDate' }}</span>
              </div>
            </article>
          }
        </div>
      }
    </section>
  `,
  styles: `
    .bookings {
      max-width: 64rem;
    }
    .bookings-heading {
      display: flex;
      gap: 1rem;
      justify-content: space-between;
      align-items: center;
      margin-bottom: 2rem;
    }
    .booking-row {
      display: grid;
      grid-template-columns: minmax(0, 1fr) auto;
      gap: 2rem;
      padding: 1.75rem 0;
      border-top: 1px solid var(--color-border);
    }
    .booking-route h2 {
      display: inline-block;
      margin-right: 1rem;
    }
    .booking-route p {
      margin: 0.5rem 0;
      font-size: 0.85rem;
      color: var(--color-muted);
    }
    .booking-meta {
      display: grid;
      gap: 0.4rem;
      align-content: start;
      text-align: right;
      font-variant-numeric: tabular-nums;
    }
    .booking-meta strong {
      font-size: 1.35rem;
    }
    .booking-meta span {
      font-size: 0.8rem;
      color: var(--color-muted);
    }
    @media (max-width: 600px) {
      .bookings-heading {
        align-items: flex-start;
        flex-direction: column;
      }
      .booking-row {
        grid-template-columns: 1fr;
        gap: 1rem;
      }
      .booking-meta {
        text-align: left;
      }
    }
  `,
})
export class BookingsPage {
  private readonly http = inject(HttpClient);
  readonly bookings = signal<Booking[]>([]);
  readonly loading = signal(true);
  readonly error = signal('');

  constructor() {
    this.load();
  }

  load(): void {
    this.loading.set(true);
    this.error.set('');
    this.http.get<Booking[]>('/api/reservation/bookings').subscribe({
      next: (bookings) => {
        this.bookings.set(bookings);
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Your bookings could not be loaded. Please try again.');
      },
    });
  }

  statusLabel(status: string): string {
    switch (status) {
      case 'PendingPayment':
        return 'Pending payment';
      case 'PaymentOrphaned':
        return 'Payment needs review';
      case 'Confirmed':
        return 'Confirmed';
      case 'Expired':
        return 'Expired';
      default:
        return status;
    }
  }
}
