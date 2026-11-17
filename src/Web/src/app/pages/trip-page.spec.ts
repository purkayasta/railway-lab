import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { AuthService } from '../auth/auth.service';
import { TripPage } from './trip-page';

class FakeSocket {
  static latest: FakeSocket;
  onopen: (() => void) | null = null;
  onmessage: ((event: { data: string }) => void) | null = null;
  onerror: (() => void) | null = null;
  onclose: (() => void) | null = null;
  send = vi.fn();
  constructor() {
    FakeSocket.latest = this;
  }
  close(): void {
    this.onclose?.();
  }
}

describe('TripPage updates', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('refetches seats after joining and when another user holds a seat', () => {
    vi.stubGlobal('WebSocket', FakeSocket);
    TestBed.configureTestingModule({
      imports: [TripPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        { provide: AuthService, useValue: { accessToken: () => null, isLoggedIn: () => false } },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              paramMap: convertToParamMap({ id: 'trip-1' }),
              queryParamMap: convertToParamMap({ from: 'DHA', to: 'CTG' }),
            },
          },
        },
      ],
    });
    const fixture = TestBed.createComponent(TripPage);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/reservation/seat-limit').flush({ maxSeatsPerUserPerTrip: 1 });
    http.expectOne('/api/schedule/trips/trip-1/layout').flush({
      coaches: [
        {
          label: 'A',
          classCode: 'economy',
          seats: [
            { publicId: 'seat-1', label: '1A' },
            { publicId: 'seat-2', label: '1B' },
          ],
        },
      ],
    });
    http
      .expectOne('/api/reservation/trips/trip-1/seats')
      .flush([{ seatPublicId: 'seat-1', status: 'available', mine: false }]);
    const socket = FakeSocket.latest;
    socket.onopen?.();
    socket.onmessage?.({ data: '{}\u001e' });
    socket.onmessage?.({ data: '{"type":3,"invocationId":"join"}\u001e' });
    http
      .expectOne('/api/reservation/trips/trip-1/seats')
      .flush([{ seatPublicId: 'seat-1', status: 'available', mine: false }]);
    fixture.componentInstance.toggle('seat-1');
    fixture.componentInstance.toggle('seat-2');
    expect(fixture.componentInstance.selected()).toEqual(['seat-1']);
    expect(fixture.componentInstance.error()).toContain('up to 1 seat');
    socket.onmessage?.({ data: '{"type":1,"target":"SeatDeltas","arguments":[]}\u001e' });
    http
      .expectOne('/api/reservation/trips/trip-1/seats')
      .flush([{ seatPublicId: 'seat-1', status: 'held', mine: false }]);
    fixture.detectChanges();
    expect(fixture.componentInstance.selected()).toEqual([]);
    expect(fixture.nativeElement.querySelector('.seats button').disabled).toBe(true);
    fixture.destroy();
    http.verify();
  });
  it.each(['{"unexpected":true}', '["business-seat","economy-seat","economy-seat",42]'])(
    'recovers a valid selection from saved state %s',
    (saved) => {
      vi.stubGlobal('WebSocket', FakeSocket);
      sessionStorage.setItem('railway-selection:trip-1:DHA:CTG', saved);
      TestBed.configureTestingModule({
        imports: [TripPage],
        providers: [
          provideHttpClient(),
          provideHttpClientTesting(),
          provideRouter([]),
          { provide: AuthService, useValue: { accessToken: () => null, isLoggedIn: () => false } },
          {
            provide: ActivatedRoute,
            useValue: {
              snapshot: {
                paramMap: convertToParamMap({ id: 'trip-1' }),
                queryParamMap: convertToParamMap({ from: 'DHA', to: 'CTG', class: 'economy' }),
              },
            },
          },
        ],
      });
      const fixture = TestBed.createComponent(TripPage);
      const http = TestBed.inject(HttpTestingController);
      http.expectOne('/api/reservation/seat-limit').flush({ maxSeatsPerUserPerTrip: 4 });
      http.expectOne('/api/schedule/trips/trip-1/layout').flush({
        coaches: [
          { label: 'A', classCode: 'economy', seats: [{ publicId: 'economy-seat', label: '1A' }] },
          {
            label: 'B',
            classCode: 'business',
            seats: [{ publicId: 'business-seat', label: '1A' }],
          },
        ],
      });
      http.expectOne('/api/reservation/trips/trip-1/seats').flush([
        { seatPublicId: 'economy-seat', status: 'available', mine: false },
        { seatPublicId: 'business-seat', status: 'available', mine: false },
      ]);
      expect(fixture.componentInstance.selected()).toEqual(
        saved.startsWith('[') ? ['economy-seat'] : [],
      );
      expect(fixture.componentInstance.selectedLabels()).toBe(saved.startsWith('[') ? 'A 1A' : '');
      fixture.destroy();
      sessionStorage.clear();
      http.verify();
    },
  );
});
