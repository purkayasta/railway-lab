import { Component, DestroyRef, inject, signal } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { forkJoin } from 'rxjs';
import { AuthService } from '../auth/auth.service';

interface Seat {
  publicId: string;
  label: string;
}
interface Coach {
  label: string;
  classCode: string;
  seats: Seat[];
}
interface Layout {
  coaches: Coach[];
}
interface SeatStatus {
  seatPublicId: string;
  status: string;
  mine: boolean;
}
interface Hold {
  holdGroupPublicId: string;
}

@Component({
  selector: 'app-trip-page',
  imports: [RouterLink],
  template: `
    <section class="flow-page trip-page">
      <a class="back-link" routerLink="/">Back to search</a>
      <ol class="journey-steps" aria-label="Booking steps">
        <li>Find a train</li>
        <li aria-current="step">Choose seats</li>
        <li>Confirm booking</li>
      </ol>
      <h1>Choose your seat</h1>
      @if (origin && destination) {
        <p class="page-description">
          {{ origin }} → {{ destination }} · {{ travelClass || 'All classes' }}
        </p>
      } @else {
        <p role="alert">Return to search and choose your origin and destination.</p>
      }
      @if (error()) {
        <p class="message" role="alert">{{ error() }}</p>
      }
      @if (loading()) {
        <p class="message" role="status">Loading seats…</p>
      }
      <div class="seat-workspace">
        <div>
          <ul class="seat-legend" aria-label="Seat status legend">
            <li><span class="legend-seat available" aria-hidden="true"></span>Available</li>
            <li><span class="legend-seat selected" aria-hidden="true"></span>Selected</li>
            <li><span class="legend-seat" aria-hidden="true"></span>Unavailable</li>
            <li><span class="legend-seat mine" aria-hidden="true"></span>Your hold</li>
          </ul>
          @for (coach of coaches(); track coach.label) {
            <section class="coach panel">
              <div class="coach-heading">
                <h2>Coach {{ coach.label }}</h2>
                <span class="status-badge">{{ coach.classCode }}</span>
              </div>
              <div class="seats">
                @for (seat of coach.seats; track seat.publicId) {
                  <button
                    type="button"
                    [disabled]="status(seat.publicId) !== 'available' || busy()"
                    [attr.data-status]="status(seat.publicId)"
                    [attr.aria-pressed]="selected().includes(seat.publicId)"
                    [attr.aria-label]="
                      'Coach ' + coach.label + ', seat ' + seat.label + ', ' + status(seat.publicId)
                    "
                    (click)="toggle(seat.publicId)"
                  >
                    {{ seat.label }}
                  </button>
                }
              </div>
            </section>
          }
          @if (!loading() && !error() && coaches().length === 0) {
            <p class="message">
              No coaches are available for this class. Return to search to choose another journey.
            </p>
          }
        </div>
        <aside class="selection-summary panel" aria-labelledby="selection-title">
          <h2 id="selection-title">Your selection</h2>
          <p class="selection-count" aria-live="polite">
            {{ selected().length }} {{ selected().length === 1 ? 'seat' : 'seats' }} selected
          </p>
          <p class="selection-labels">
            {{ selectedLabels() || 'Choose available seats from a coach.' }}
          </p>
          @if (seatLimit(); as limit) {
            <p>Up to {{ limit }} seats per trip, including confirmed tickets.</p>
          }
          <p>Seats are reserved after you continue. Availability may change while you choose.</p>
          <button
            type="button"
            class="primary-button"
            [disabled]="!selected().length || !origin || !destination || busy()"
            (click)="hold()"
          >
            {{
              busy() ? 'Holding seats…' : auth.isLoggedIn() ? 'Reserve seats' : 'Sign in to reserve'
            }}
          </button>
        </aside>
      </div>
    </section>
  `,
  styles: `
    .seat-workspace {
      display: grid;
      grid-template-columns: minmax(0, 1fr) 20rem;
      gap: 2rem;
      align-items: start;
    }
    .seat-legend {
      display: flex;
      flex-wrap: wrap;
      gap: 0.75rem 1.25rem;
      padding: 0;
      margin: 0 0 1.25rem;
      list-style: none;
      font-size: 0.8rem;
      color: var(--color-muted);
    }
    .seat-legend li {
      display: flex;
      align-items: center;
      gap: 0.45rem;
    }
    .legend-seat {
      width: 1rem;
      height: 1rem;
      border: 1px solid var(--color-muted);
      border-radius: 0.2rem;
      background: var(--color-surface-raised);
    }
    .legend-seat.available {
      background: var(--color-positive-soft);
      border-color: var(--color-positive);
    }
    .legend-seat.selected {
      background: var(--color-accent-soft);
      border: 2px solid var(--color-accent);
    }
    .legend-seat.mine {
      background: var(--color-accent);
      border-color: var(--color-accent);
    }
    .coach {
      margin-bottom: 1.5rem;
    }
    .coach-heading {
      display: flex;
      align-items: baseline;
      gap: 1rem;
    }
    .coach-heading .status-badge {
      text-transform: capitalize;
    }
    .seats {
      display: grid;
      grid-template-columns: repeat(4, minmax(2.75rem, 4rem));
      gap: 0.6rem;
      justify-content: center;
      max-width: 25rem;
      margin: 1.5rem auto 0;
    }
    .seats button {
      min-height: 3rem;
      border: 1px solid var(--color-positive);
      border-radius: 0.4rem;
      background: var(--color-positive-soft);
      color: var(--color-positive);
      font-weight: 650;
      cursor: pointer;
    }
    .seats button:nth-child(4n + 2) {
      margin-right: 0.4rem;
    }
    .seats button:nth-child(4n + 3) {
      margin-left: 0.4rem;
    }
    .seats button:hover:not(:disabled),
    .seats button[aria-pressed='true'] {
      background: var(--color-accent-soft);
      color: var(--color-accent);
      border-color: var(--color-accent);
    }
    .seats button[aria-pressed='true'] {
      outline: 2px solid var(--color-accent);
      outline-offset: 2px;
    }
    .seats button:disabled {
      opacity: 1;
      cursor: not-allowed;
      color: var(--color-muted);
      border-color: var(--color-border);
      background: var(--color-surface-raised);
      text-decoration: line-through;
    }
    .seats button[data-status='mine'] {
      background: var(--color-accent);
      color: var(--color-on-accent);
      text-decoration: none;
    }
    .selection-summary {
      position: sticky;
      top: 1.5rem;
    }
    .selection-summary p {
      font-size: 0.85rem;
      color: var(--color-muted);
    }
    .selection-summary .selection-count {
      font-size: 1.1rem;
      font-weight: 650;
      color: var(--color-ink);
    }
    .selection-summary .primary-button {
      width: 100%;
      margin-top: 1rem;
    }
    @media (max-width: 850px) {
      .seat-workspace {
        grid-template-columns: 1fr;
      }
      .selection-summary {
        grid-row: 1;
        position: sticky;
        top: 0.5rem;
        z-index: 2;
        display: grid;
        grid-template-columns: minmax(0, 1fr) auto;
        gap: 0.25rem 1rem;
        padding: 1rem;
      }
      .selection-summary h2 {
        margin: 0;
        font-size: 0.85rem;
      }
      .selection-summary p {
        margin: 0;
        grid-column: 1;
      }
      .selection-summary .selection-count {
        font-size: 0.9rem;
      }
      .selection-summary .selection-labels {
        font-size: 0.75rem;
      }
      .selection-summary p:not(.selection-count):not(.selection-labels) {
        display: none;
      }
      .selection-summary .primary-button {
        grid-column: 2;
        grid-row: 1 / 4;
        width: auto;
        margin: 0;
        max-width: 10rem;
      }
    }
  `,
})
export class TripPage {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  protected readonly auth = inject(AuthService);
  private readonly destroyRef = inject(DestroyRef);
  readonly tripId = this.route.snapshot.paramMap.get('id') ?? '';
  readonly origin = this.route.snapshot.queryParamMap.get('from') ?? '';
  readonly destination = this.route.snapshot.queryParamMap.get('to') ?? '';
  readonly travelClass = this.route.snapshot.queryParamMap.get('class') ?? '';
  readonly coaches = signal<Coach[]>([]);
  readonly statuses = signal<SeatStatus[]>([]);
  readonly selected = signal<string[]>([]);
  readonly loading = signal(true);
  readonly busy = signal(false);
  readonly error = signal('');
  readonly seatLimit = signal<number | null>(null);
  private readonly selectionKey = `railway-selection:${this.tripId}:${this.origin}:${this.destination}`;
  private socket: WebSocket | null = null;
  private reconnectTimer: ReturnType<typeof setTimeout> | undefined;
  private heartbeat: ReturnType<typeof setInterval> | undefined;
  private destroyed = false;

  constructor() {
    try {
      const saved: unknown = JSON.parse(sessionStorage.getItem(this.selectionKey) ?? '[]');
      this.selected.set(
        Array.isArray(saved)
          ? [...new Set(saved.filter((id): id is string => typeof id === 'string'))]
          : [],
      );
    } catch {
      this.selected.set([]);
    }

    forkJoin({
      layout: this.http.get<Layout>(`/api/schedule/trips/${this.tripId}/layout`),
      statuses: this.http.get<SeatStatus[]>(`/api/reservation/trips/${this.tripId}/seats`),
    }).subscribe({
      next: ({ layout, statuses }) => {
        this.coaches.set(
          layout.coaches.filter(
            (coach) => !this.travelClass || coach.classCode === this.travelClass,
          ),
        );
        this.statuses.set(statuses);
        const visibleIds = new Set(
          this.coaches().flatMap((coach) => coach.seats.map((seat) => seat.publicId)),
        );
        this.selected.update((ids) =>
          ids.filter(
            (id) =>
              visibleIds.has(id) &&
              statuses.some(
                (seat) => seat.seatPublicId === id && seat.status === 'available' && !seat.mine,
              ),
          ),
        );
        this.loading.set(false);
      },
      error: () => {
        this.loading.set(false);
        this.error.set('Seats could not be loaded. Please try again.');
      },
    });

    this.connectUpdates();
    this.http.get<{ maxSeatsPerUserPerTrip: number }>('/api/reservation/seat-limit').subscribe({
      next: (policy) => {
        if (Number.isInteger(policy.maxSeatsPerUserPerTrip) && policy.maxSeatsPerUserPerTrip > 0) {
          this.seatLimit.set(policy.maxSeatsPerUserPerTrip);
          this.selected.update((ids) => ids.slice(0, policy.maxSeatsPerUserPerTrip));
        }
      },
      error: () => {},
    });
    this.destroyRef.onDestroy(() => {
      this.destroyed = true;
      clearTimeout(this.reconnectTimer);
      clearInterval(this.heartbeat);
      this.socket?.close();
    });
  }

  private connectUpdates(): void {
    const protocol = location.protocol === 'https:' ? 'wss:' : 'ws:';
    const token = this.auth.accessToken();
    const query = token ? `?access_token=${encodeURIComponent(token)}` : '';
    const socket = new WebSocket(`${protocol}//${location.host}/hubs/seats${query}`);
    this.socket = socket;
    let ready = false;
    let pending = '';

    socket.onopen = () => socket.send('{"protocol":"json","version":1}\u001e');
    socket.onmessage = (event) => {
      pending += String(event.data);
      const messages = pending.split('\u001e');
      pending = messages.pop() ?? '';
      for (const raw of messages) {
        try {
          const message = JSON.parse(raw);
          if (!ready) {
            if (message.error) {
              socket.close();
              return;
            }
            ready = true;
            socket.send(
              JSON.stringify({
                type: 1,
                invocationId: 'join',
                target: 'JoinTrip',
                arguments: [this.tripId],
              }) + '\u001e',
            );
            this.heartbeat = setInterval(() => socket.send('{"type":6}\u001e'), 15000);
          } else if (message.type === 3 && message.invocationId === 'join') {
            if (message.error) {
              socket.close();
              return;
            }
            this.refreshStatuses();
          } else if (message.type === 1 && message.target === 'SeatDeltas') {
            this.refreshStatuses();
          }
        } catch {
          socket.close();
        }
      }
    };
    socket.onerror = () => socket.close();
    socket.onclose = () => {
      clearInterval(this.heartbeat);
      if (!this.destroyed) {
        this.reconnectTimer = setTimeout(() => this.connectUpdates(), 3000);
      }
    };
  }

  private refreshStatuses(): void {
    this.http.get<SeatStatus[]>(`/api/reservation/trips/${this.tripId}/seats`).subscribe({
      next: (statuses) => {
        this.statuses.set(statuses);
        this.selected.update((ids) =>
          ids.filter((id) =>
            statuses.some((seat) => seat.seatPublicId === id && seat.status === 'available'),
          ),
        );
      },
      error: () => this.error.set('Seat updates are temporarily unavailable. Please try again.'),
    });
  }

  status(id: string): string {
    const seat = this.statuses().find((value) => value.seatPublicId === id);
    return seat?.mine ? 'mine' : (seat?.status ?? 'unavailable');
  }

  selectedLabels(): string {
    const selected = new Set(this.selected());
    return this.coaches()
      .flatMap((coach) =>
        coach.seats
          .filter((seat) => selected.has(seat.publicId))
          .map((seat) => `${coach.label} ${seat.label}`),
      )
      .join(', ');
  }

  toggle(id: string): void {
    const limit = this.seatLimit();
    if (!this.selected().includes(id) && limit !== null && this.selected().length >= limit) {
      this.error.set(`You may select up to ${limit} ${limit === 1 ? 'seat' : 'seats'} per trip.`);
      return;
    }

    if (this.status(id) !== 'available' || this.busy()) return;

    this.error.set('');
    this.selected.update((ids) =>
      ids.includes(id) ? ids.filter((value) => value !== id) : [...ids, id],
    );
    sessionStorage.setItem(this.selectionKey, JSON.stringify(this.selected()));
  }

  hold(): void {
    if (!this.auth.isLoggedIn()) {
      this.auth.login(this.router.url);
      return;
    }

    this.busy.set(true);
    this.error.set('');
    this.http
      .post<Hold>(`/api/reservation/trips/${this.tripId}/holds`, {
        seatPublicIds: this.selected(),
        origin: this.origin,
        destination: this.destination,
      })
      .subscribe({
        next: (hold) => {
          sessionStorage.removeItem(this.selectionKey);
          this.router.navigate(['/checkout', hold.holdGroupPublicId]);
        },
        error: () => {
          this.busy.set(false);
          this.error.set(
            'Seats could not be held. They may have been taken or the selection may exceed the limit.',
          );
        },
      });
  }
}
