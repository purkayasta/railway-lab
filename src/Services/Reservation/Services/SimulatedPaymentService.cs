namespace Reservation.Services;

// Temporary in-process simulation. It never takes money or contacts a payment provider.
public sealed class SimulatedPaymentService(PaymentApprovalService approvals)
{
    public Task<string?> Approve(Guid bookingId, CancellationToken cancellationToken = default) =>
        approvals.ApplyApproval(bookingId, cancellationToken);
}
