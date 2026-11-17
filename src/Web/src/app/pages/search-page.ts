import { Component, DestroyRef, inject, signal } from '@angular/core';
import { HttpClient, HttpParams } from '@angular/common/http';
import { DatePipe, DecimalPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { FormControl, FormGroup, ReactiveFormsModule, Validators } from '@angular/forms';
import { takeUntilDestroyed } from '@angular/core/rxjs-interop';
import { Subscription } from 'rxjs';

interface Station {
  code: string;
  name: string;
}
interface Trip {
  publicId: string;
  trainNumber: string;
  trainName: string;
  departureUtc: string;
  arrivalUtc: string;
  fare: number;
  classCode: string;
  availableSeats: number | null;
  availabilityUnknown: boolean;
}

function localDateInputValue(): string {
  const date = new Date();
  const month = String(date.getMonth() + 1).padStart(2, '0');
  const day = String(date.getDate()).padStart(2, '0');
  return `${date.getFullYear()}-${month}-${day}`;
}

@Component({
  selector: 'app-search-page',
  imports: [ReactiveFormsModule, DatePipe, DecimalPipe, RouterLink],
  template: `
    <section class="search-page">
      <div class="page-intro">
        <h1>Your next journey<br />starts here.</h1>
        <p class="intro-copy">
          Find a train, choose your seat, and keep every part of your trip in one place.
        </p>
        <ol class="journey-steps" aria-label="Booking steps">
          <li aria-current="step">Find a train</li>
          <li>Choose seats</li>
          <li>Confirm booking</li>
        </ol>
        <p class="browse-note">Browse without an account. Sign in when you’re ready to reserve.</p>
      </div>

      <section class="search-panel" aria-labelledby="search-title">
        <div class="search-panel__heading">
          <h2 id="search-title">Where to next?</h2>
          <span class="status-badge">Public search</span>
        </div>
        <form [formGroup]="searchForm" (ngSubmit)="search()" novalidate>
          <div class="station-fields">
            <label
              ><span>From</span>
              <select formControlName="from">
                <option value="">Choose station</option>
                @for (station of stations(); track station.code) {
                  <option [value]="station.code">{{ station.name }}</option>
                }
              </select>
            </label>
            <label
              ><span>To</span>
              <select formControlName="to">
                <option value="">Choose station</option>
                @for (station of stations(); track station.code) {
                  <option [value]="station.code">{{ station.name }}</option>
                }
              </select>
            </label>
          </div>
          <div class="trip-fields">
            <label><span>Travel date</span><input formControlName="date" type="date" /></label>
            <label
              ><span>Class</span>
              <select formControlName="travelClass">
                <option value="economy">Economy</option>
                <option value="business">Business</option>
              </select>
            </label>
          </div>
          @if (searchForm.invalid && searchAttempted()) {
            <p class="form-error" role="alert">Add both stations and a travel date to continue.</p>
          }
          <button class="primary-button" type="submit" [disabled]="loading()">
            {{ loading() ? 'Searching schedules…' : 'Search trains' }}
          </button>
        </form>
        @if (stationError()) {
          <div class="message" role="alert">
            <p>{{ stationError() }}</p>
            <button class="text-button" type="button" (click)="loadStations()">
              Retry loading stations
            </button>
          </div>
        }
      </section>

      @if (searchSubmitted()) {
        <section
          class="search-results"
          aria-labelledby="results-title"
          [attr.aria-busy]="loading()"
        >
          <div class="results-heading">
            <h2 id="results-title">Your trains</h2>
            @if (submittedJourney(); as journey) {
              <p>{{ journey.from }} → {{ journey.to }} · {{ journey.date | date: 'mediumDate' }}</p>
            }
          </div>
          @if (loading()) {
            <p class="message" role="status">Finding trains for your journey…</p>
          } @else if (error()) {
            <p class="message form-error" role="alert">{{ error() }}</p>
          } @else if (trips().length === 0) {
            <div class="empty-state" role="status">
              <h3>No trains for this journey</h3>
              <p>Try another date or a different pair of stations.</p>
            </div>
          } @else {
            <p class="result-count" role="status">
              {{ trips().length }} {{ trips().length === 1 ? 'train' : 'trains' }} found · Times
              shown in your local timezone
            </p>
            <div class="trip-list">
              @for (trip of trips(); track trip.publicId) {
                <article class="train-result">
                  <div class="train-name">
                    <h3>{{ trip.trainName }}</h3>
                    <p>Train {{ trip.trainNumber }} · {{ trip.classCode }}</p>
                  </div>
                  <div class="train-times">
                    <time [attr.datetime]="trip.departureUtc">{{
                      trip.departureUtc | date: 'shortTime'
                    }}</time
                    ><span aria-hidden="true">→</span
                    ><time [attr.datetime]="trip.arrivalUtc">{{
                      trip.arrivalUtc | date: 'shortTime'
                    }}</time
                    ><small>{{ trip.arrivalUtc | date: 'mediumDate' }} arrival</small>
                  </div>
                  <div class="train-fare">
                    <strong>{{ trip.fare | number: '1.2-2' }}</strong
                    ><span>per seat · demo fare</span>
                    @if (trip.availabilityUnknown) {
                      <span>Availability unavailable</span>
                    } @else {
                      <span>{{ trip.availableSeats }} seats available</span>
                    }
                  </div>
                  <a
                    class="secondary-button"
                    [routerLink]="['/trips', trip.publicId]"
                    [queryParams]="{
                      from: submittedJourney()?.from,
                      to: submittedJourney()?.to,
                      class: trip.classCode,
                    }"
                    >View trip<span class="visually-hidden">: {{ trip.trainName }}</span></a
                  >
                </article>
              }
            </div>
          }
        </section>
      }
    </section>
  `,
  styleUrl: './search-page.css',
})
export class SearchPage {
  private readonly http = inject(HttpClient);
  private readonly destroyRef = inject(DestroyRef);
  private searchRequest?: Subscription;
  readonly stations = signal<Station[]>([]);
  readonly trips = signal<Trip[]>([]);
  readonly loading = signal(false);
  readonly error = signal('');
  readonly stationError = signal('');
  readonly searchAttempted = signal(false);
  readonly searchSubmitted = signal(false);
  readonly submittedJourney = signal<{ from: string; to: string; date: string } | null>(null);
  readonly searchForm = new FormGroup({
    from: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    to: new FormControl('', { nonNullable: true, validators: [Validators.required] }),
    date: new FormControl(localDateInputValue(), {
      nonNullable: true,
      validators: [Validators.required],
    }),
    travelClass: new FormControl('economy', { nonNullable: true }),
  });

  constructor() {
    this.loadStations();
  }

  loadStations(): void {
    this.stationError.set('');
    this.http
      .get<Station[]>('/api/schedule/stations')
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (stations) => this.stations.set(stations),
        error: () => this.stationError.set('Stations could not be loaded. Please try again.'),
      });
  }

  search(): void {
    this.searchAttempted.set(true);
    if (this.searchForm.invalid) return;

    const { from, to, date, travelClass } = this.searchForm.getRawValue();
    this.searchRequest?.unsubscribe();
    this.submittedJourney.set({ from, to, date });
    const params = new HttpParams()
      .set('from', from)
      .set('to', to)
      .set('date', date)
      .set('class', travelClass);
    this.searchSubmitted.set(true);
    this.loading.set(true);
    this.error.set('');
    this.searchRequest = this.http
      .get<Trip[]>('/api/schedule/trips/search', { params })
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe({
        next: (trips) => {
          this.trips.set(trips);
          this.loading.set(false);
        },
        error: () => {
          this.error.set('Schedules could not be loaded. Please try searching again.');
          this.loading.set(false);
        },
      });
  }
}
