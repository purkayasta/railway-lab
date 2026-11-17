const rolePermissions: Readonly<Record<string, readonly string[]>> = {
  passenger: ['seats:hold', 'bookings:read-own', 'payments:pay', 'helpdesk:chat', 'sessions:revoke-own'],
  'station-agent': [
    'seats:hold',
    'bookings:read-own',
    'payments:pay',
    'helpdesk:chat',
    'bookings:create-on-behalf',
    'bookings:read-any',
    'sessions:revoke-own',
  ],
  operations: ['reports:read', 'payments:ops-read', 'bookings:read-any', 'policy:read', 'sessions:revoke-own'],
  admin: [
    'seats:hold',
    'bookings:read-own',
    'bookings:read-any',
    'bookings:create-on-behalf',
    'policy:read',
    'policy:write',
    'payments:pay',
    'payments:ops-read',
    'reports:read',
    'helpdesk:chat',
    'auth:read',
    'auth:manage',
    'sessions:revoke-own',
  ],
};

export function permissionsForRoles(roles: readonly string[]): ReadonlySet<string> {
  const permissions = new Set<string>();
  for (const role of roles) {
    for (const permission of rolePermissions[role] ?? []) {
      permissions.add(permission);
    }
  }
  return permissions;
}
