import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ActivatedRoute, convertToParamMap, provideRouter } from '@angular/router';
import { CheckoutPage } from './checkout-page';

describe('CheckoutPage', () => {
  it('displays the server quote and confirms without sending a price', async () => {
    await TestBed.configureTestingModule({
      imports: [CheckoutPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'hold-1' }) } },
        },
      ],
    }).compileComponents();

    const fixture = TestBed.createComponent(CheckoutPage);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/reservation/holds/hold-1').flush({
      holdGroupPublicId: 'hold-1',
      tripPublicId: 'trip-1',
      expiresAtUtc: '2030-01-01T10:00:00Z',
      seatPublicIds: ['seat-1'],
      origin: 'DHA',
      destination: 'CTG',
      totalAmount: 123,
    });
    http.expectOne('/api/schedule/trips/trip-1/layout').flush({
      coaches: [{ label: 'A', seats: [{ publicId: 'seat-1', label: '1A' }] }],
    });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('123.00');
    expect(fixture.nativeElement.textContent).toContain('A 1A');

    fixture.componentInstance.confirm();
    const request = http.expectOne('/api/reservation/holds/hold-1/checkout');
    expect(request.request.body).toEqual({});
    request.flush({ publicId: 'booking-1', status: 'Confirmed', totalAmount: 123 });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Booking confirmed');
    expect(fixture.nativeElement.textContent).toContain('Payment was simulated');
    http.verify();
  });

  it('shows the countdown and accepts an extension', () => {
    TestBed.configureTestingModule({
      imports: [CheckoutPage],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        provideRouter([]),
        {
          provide: ActivatedRoute,
          useValue: { snapshot: { paramMap: convertToParamMap({ id: 'hold-1' }) } },
        },
      ],
    });
    const fixture = TestBed.createComponent(CheckoutPage);
    const http = TestBed.inject(HttpTestingController);
    const quote = {
      holdGroupPublicId: 'hold-1',
      tripPublicId: 'trip-1',
      expiresAtUtc: '2030-01-01T10:00:00Z',
      seatPublicIds: ['seat-1'],
      origin: 'DHA',
      destination: 'CTG',
      totalAmount: 123,
    };
    http.expectOne('/api/reservation/holds/hold-1').flush(quote);
    http.expectOne('/api/schedule/trips/trip-1/layout').flush({ coaches: [] });
    fixture.componentInstance.now.set(Date.parse('2030-01-01T09:58:30Z'));
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('1:30 remaining');

    fixture.componentInstance.extend();
    http
      .expectOne('/api/reservation/holds/hold-1/extend')
      .flush({ ...quote, expiresAtUtc: '2030-01-01T10:05:00Z' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('6:30 remaining');
    expect(fixture.componentInstance.extended()).toBe(true);
    fixture.componentInstance.now.set(Date.parse('2030-01-01T10:05:00Z'));
    expect(fixture.componentInstance.expired()).toBe(true);
    http.verify();
  });
});
