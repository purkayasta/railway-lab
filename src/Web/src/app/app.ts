import { DOCUMENT } from '@angular/common';
import { Component, inject, signal } from '@angular/core';
import { Router, RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './auth/auth.service';
import { ToastService } from './toast.service';

@Component({
  imports: [RouterLink, RouterLinkActive, RouterOutlet],
  selector: 'app-root',
  templateUrl: './app.html',
  styleUrl: './app.css',
})
export class App {
  readonly auth = inject(AuthService);
  readonly toast = inject(ToastService);
  readonly router = inject(Router);
  private readonly document = inject(DOCUMENT);
  readonly theme = signal<'light' | 'dark'>('light');

  toggleTheme(): void {
    const nextTheme = this.theme() === 'light' ? 'dark' : 'light';
    this.theme.set(nextTheme);
    this.document.documentElement.dataset['theme'] = nextTheme;
  }
}
