import { Routes } from '@angular/router';
import { authGuard, permissionGuard } from './auth/auth.guard';
import { SearchPage } from './pages/search-page';
import { TripPage } from './pages/trip-page';
import { CheckoutPage } from './pages/checkout-page';
import { BookingsPage } from './pages/bookings-page';
import {
  CallbackPage,
  ForbiddenPage,
  HelpdeskPage,
  NotFoundPage,
} from './pages/status-pages';

export const routes: Routes = [
  { path: '', component: SearchPage, pathMatch: 'full' },
  { path: 'trips/:id', component: TripPage },
  { path: 'checkout/:id', component: CheckoutPage, canActivate: [authGuard] },
  { path: 'my-bookings', component: BookingsPage, canActivate: [authGuard] },
  {
    path: 'admin/auth',
    canActivate: [permissionGuard('auth:read')],
    loadComponent: () => import('./pages/auth-admin-page').then((page) => page.AuthAdminPage),
  },
  {
    path: 'admin',
    pathMatch: 'full',
    canActivate: [permissionGuard('reports:read')],
    loadComponent: () => import('./pages/admin-page').then((page) => page.AdminPage),
  },
  { path: 'helpdesk', component: HelpdeskPage, canActivate: [authGuard] },
  { path: 'forbidden', component: ForbiddenPage },
  { path: 'callback', component: CallbackPage },
  { path: '**', component: NotFoundPage },
];
