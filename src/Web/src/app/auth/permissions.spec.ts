import { permissionsForRoles } from './permissions';

describe('permissionsForRoles', () => {
  it('unions the permissions granted by multiple roles', () => {
    expect(permissionsForRoles(['passenger', 'operations'])).toEqual(
      new Set([
        'seats:hold',
        'bookings:read-own',
        'payments:pay',
        'helpdesk:chat',
        'sessions:revoke-own',
        'reports:read',
        'payments:ops-read',
        'bookings:read-any',
        'policy:read',
      ]),
    );
  });

  it('grants no permissions to unknown roles', () => {
    expect(permissionsForRoles(['unknown-role'])).toEqual(new Set());
  });
});
