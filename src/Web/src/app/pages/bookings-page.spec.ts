import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { BookingsPage } from './bookings-page';

describe('BookingsPage', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [BookingsPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    });
  });

  it('shows pending bookings with their reference and payment status', () => {
    const fixture = TestBed.createComponent(BookingsPage);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/reservation/bookings').flush([
      {
        publicId: 'booking-1',
        origin: 'DHA',
        destination: 'CTG',
        status: 'PendingPayment',
        totalAmount: 123,
        createdAtUtc: '2030-01-01T10:00:00Z',
        passengerName: null,
      },
    ]);
    fixture.detectChanges();

    expect(fixture.nativeElement.textContent).toContain('DHA → CTG');
    expect(fixture.nativeElement.textContent).toContain('booking-1');
    expect(fixture.nativeElement.textContent).toContain('Pending payment');
    expect(fixture.nativeElement.textContent).toContain('not yet confirmed');
    expect(fixture.nativeElement.textContent).toContain('123.00');
    http.verify();
  });

  it('allows retry after a failure and shows an empty state', () => {
    const fixture = TestBed.createComponent(BookingsPage);
    const http = TestBed.inject(HttpTestingController);
    http
      .expectOne('/api/reservation/bookings')
      .flush(null, { status: 503, statusText: 'Unavailable' });
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]').textContent).toContain(
      'could not be loaded',
    );

    fixture.nativeElement.querySelector('button').click();
    http.expectOne('/api/reservation/bookings').flush([]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('You have no bookings yet');
    http.verify();
  });
});
