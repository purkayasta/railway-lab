import { routes } from './app.routes';

describe('application routes', () => {
  it('keeps journey search and trip viewing public', () => {
    expect(routes.find((route) => route.path === '')?.canActivate).toBeUndefined();
    expect(routes.find((route) => route.path === 'trips/:id')?.canActivate).toBeUndefined();
  });

  it('protects bookings, admin dashboards, auth access, and helpdesk', () => {
    expect(routes.find((route) => route.path === 'my-bookings')?.canActivate).toBeDefined();
    expect(routes.find((route) => route.path === 'admin')?.canActivate).toBeDefined();
    expect(routes.find((route) => route.path === 'admin/auth')?.canActivate).toBeDefined();
    expect(routes.find((route) => route.path === 'helpdesk')?.canActivate).toBeDefined();
  });
});
