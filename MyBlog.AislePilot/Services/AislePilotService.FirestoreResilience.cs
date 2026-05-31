using Microsoft.Extensions.Logging;

namespace MyBlog.Services;

public sealed partial class AislePilotService
{
    private CancellationTokenSource CreateFirestoreReadBudgetCts(CancellationToken cancellationToken)
    {
        var firestoreReadBudgetCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        firestoreReadBudgetCts.CancelAfter(FirestoreReadTimeout);
        return firestoreReadBudgetCts;
    }

    private void LogFirestoreReadTimeout(string operation)
    {
        _logger?.LogWarning(
            "AislePilot Firestore read timed out during {Operation} after {TimeoutMs}ms. Continuing with runtime fallback data.",
            operation,
            FirestoreReadTimeout.TotalMilliseconds);
    }

    private void LogFirestoreReadFailure(Exception ex, string operation)
    {
        _logger?.LogWarning(
            ex,
            "AislePilot Firestore read failed during {Operation}. Continuing with runtime fallback data.",
            operation);
    }
}
