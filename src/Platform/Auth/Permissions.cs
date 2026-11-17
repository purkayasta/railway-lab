namespace Platform.Auth;

/// <summary>
/// Permission names used by endpoints. A permission is one capability; roles are bundles of permissions.
/// Code checks permissions, never role names. The catalog mirrors deploy/auth/default-role-permissions.json.
/// </summary>
public static class Permissions
{
    public const string SeatsHold = "seats:hold";
    public const string BookingsReadOwn = "bookings:read-own";
    public const string BookingsReadAny = "bookings:read-any";
    public const string BookingsCreateOnBehalf = "bookings:create-on-behalf";
    public const string PolicyRead = "policy:read";
    public const string PolicyWrite = "policy:write";
    public const string PaymentsPay = "payments:pay";
    public const string PaymentsOpsRead = "payments:ops-read";
    public const string ReportsRead = "reports:read";
    public const string HelpdeskChat = "helpdesk:chat";
    public const string AuthRead = "auth:read";
    public const string AuthManage = "auth:manage";
    public const string SessionsRevokeOwn = "sessions:revoke-own";
}
