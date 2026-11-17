import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { SearchPage } from './search-page';

describe('SearchPage', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [SearchPage],
      providers: [provideHttpClient(), provideHttpClientTesting(), provideRouter([])],
    }).compileComponents();
  });
  it('validates required journey details', () => {
    const fixture = TestBed.createComponent(SearchPage);
    TestBed.inject(HttpTestingController).expectOne('/api/schedule/stations').flush([]);
    fixture.componentInstance.search();
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('[role="alert"]')?.textContent).toContain(
      'Add both stations',
    );
  });
  it('searches for trips and shows unavailable availability', () => {
    const fixture = TestBed.createComponent(SearchPage);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/schedule/stations').flush([
      { code: 'DHA', name: 'Dhaka' },
      { code: 'CTG', name: 'Chattogram' },
    ]);
    fixture.componentInstance.searchForm.setValue({
      from: 'DHA',
      to: 'CTG',
      date: '2026-11-05',
      travelClass: 'economy',
    });
    fixture.componentInstance.search();
    const request = http.expectOne((r) => r.url === '/api/schedule/trips/search');
    expect(request.request.params.get('from')).toBe('DHA');
    expect(request.request.params.get('class')).toBe('economy');
    request.flush([
      {
        publicId: 'abc',
        trainNumber: '701',
        trainName: 'Express',
        departureUtc: '2026-11-05T06:00:00Z',
        arrivalUtc: '2026-11-05T11:00:00Z',
        fare: 600,
        classCode: 'economy',
        availableSeats: null,
        availabilityUnknown: true,
      },
    ]);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Express');
    expect(fixture.nativeElement.textContent).toContain('Availability unavailable');
    fixture.componentInstance.searchForm.controls.from.setValue('OTHER');
    fixture.detectChanges();
    expect(fixture.nativeElement.querySelector('.train-result a').getAttribute('href')).toContain(
      'from=DHA',
    );
    http.verify();
  });

  it('cancels the previous search so an older response cannot replace the current journey', () => {
    const fixture = TestBed.createComponent(SearchPage);
    const http = TestBed.inject(HttpTestingController);
    http.expectOne('/api/schedule/stations').flush([]);
    fixture.componentInstance.searchForm.setValue({
      from: 'DHA',
      to: 'CTG',
      date: '2026-11-05',
      travelClass: 'economy',
    });
    fixture.componentInstance.search();
    const first = http.expectOne((r) => r.url === '/api/schedule/trips/search');
    fixture.componentInstance.searchForm.controls.to.setValue('SYL');
    fixture.componentInstance.search();
    expect(first.cancelled).toBe(true);
    http.expectOne((r) => r.url === '/api/schedule/trips/search').flush([]);
    expect(fixture.componentInstance.submittedJourney()?.to).toBe('SYL');
    http.verify();
  });
});
