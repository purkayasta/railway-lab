import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { signal } from '@angular/core';
import { AuthService } from './auth/auth.service';
import { App } from './app';

describe('App', () => {
  const permissions = signal<ReadonlySet<string>>(new Set());
  const auth = {
    isLoggedIn: signal(false),
    user: signal(null),
    roles: signal<readonly string[]>([]),
    permissions,
    authError: signal(null),
    login: vi.fn(),
    logout: vi.fn(),
  };

  beforeEach(async () => {
    permissions.set(new Set());
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [provideRouter([]), { provide: AuthService, useValue: auth }],
    })
      .compileComponents();
  });

  it('renders the public navigation and login action', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('.brand')?.textContent).toContain('RailwayLab');
    expect(compiled.querySelector('.primary-nav a[href="/"]')?.textContent).toContain('Search');
    expect(compiled.querySelector('.sign-in')?.textContent).toContain('Sign in');
  });

  it('only displays admin navigation when the user has report access', () => {
    const fixture = TestBed.createComponent(App);
    permissions.set(new Set(['reports:read']));
    fixture.detectChanges();
    const compiled = fixture.nativeElement as HTMLElement;

    expect(compiled.querySelector('a[href="/admin"]')?.textContent).toContain('Admin');
  });
});
