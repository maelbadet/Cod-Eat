using System.Threading.Channels;

namespace CodEat.Web;

public record BookStackProgress(int Completed, int Total, string Stage);

public record BookStackSyncStatus(Guid? JobId, string State, string Message, int Completed = 0,
    int Total = 0, string Stage = "", string? DiagnosticCode = null, DateTime? StartedUtc = null, DateTime? FinishedUtc = null)
{
    public bool IsBusy => State is "Queued" or "Running";
}

// One application container owns one import at a time. Only the catalogue is persisted;
// after a process restart an interrupted import must be explicitly launched again.
public sealed class BookStackSyncQueue
{
    readonly object gate = new();
    readonly Channel<Guid> queue = Channel.CreateBounded<Guid>(new BoundedChannelOptions(1)
    { SingleReader = true, SingleWriter = false, FullMode = BoundedChannelFullMode.Wait });
    BookStackSyncStatus status = new(null, "Idle", "Aucune synchronisation lancée depuis le démarrage.");

    public BookStackSyncStatus Snapshot { get { lock (gate) return status; } }

    public bool TryEnqueue()
    {
        lock (gate)
        {
            if (status.IsBusy) return false;
            var id = Guid.NewGuid();
            if (!queue.Writer.TryWrite(id)) return false;
            status = new(id, "Queued", "Synchronisation en attente de démarrage.");
            return true;
        }
    }

    public ValueTask<Guid> ReadAsync(CancellationToken ct) => queue.Reader.ReadAsync(ct);

    public void Started(Guid id)
    {
        lock (gate)
            if (status.JobId == id) status = status with
            { State = "Running", Message = "Synchronisation en cours. Vous pouvez quitter cette page.", StartedUtc = DateTime.UtcNow, Stage = "Connexion à BookStack" };
    }

    public void Report(Guid id, BookStackProgress progress)
    {
        lock (gate)
            if (status.JobId == id && status.IsBusy) status = status with
            { Completed = progress.Completed, Total = progress.Total, Stage = progress.Stage };
    }

    public void Succeeded(Guid id, string summary)
    {
        lock (gate)
            if (status.JobId == id) status = status with
            { State = "Succeeded", Message = summary, Stage = "Synchronisation terminée", FinishedUtc = DateTime.UtcNow };
    }

    public void Failed(Guid id, BookStackDiagnostic diagnostic)
    {
        lock (gate)
            if (status.JobId == id) status = status with
            { State = "Failed", Message = $"Synchronisation échouée [{diagnostic.Code}]. {diagnostic.Guidance} Aucun import partiel enregistré.",
                DiagnosticCode = diagnostic.Code, Stage = "Synchronisation arrêtée", FinishedUtc = DateTime.UtcNow };
    }
}

public sealed class BookStackSyncWorker(BookStackSyncQueue queue, IServiceScopeFactory scopes,
    ILogger<BookStackSyncWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            Guid id;
            try { id = await queue.ReadAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }

            queue.Started(id);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
            deadline.CancelAfter(TimeSpan.FromMinutes(30));
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var summary = await scope.ServiceProvider.GetRequiredService<BookStackSync>()
                    .Run(deadline.Token, progress => queue.Report(id, progress));
                queue.Succeeded(id, summary);
                logger.LogInformation("Synchronisation BookStack terminée, tâche {JobId}.", id);
            }
            catch (Exception failure)
            {
                var diagnostic = failure is OperationCanceledException && deadline.IsCancellationRequested
                    ? stoppingToken.IsCancellationRequested
                        ? new BookStackDiagnostic("BS_STOPPED", "L’application a été arrêtée. Relancez la synchronisation après son redémarrage.")
                        : new BookStackDiagnostic("BS_SYNC_TIMEOUT", "La synchronisation a dépassé la durée totale de 30 minutes. Vérifiez la disponibilité de BookStack.")
                    : BookStackDiagnostic.From(failure);
                queue.Failed(id, diagnostic);
                // Do not log the external exception body, URI, token or response payload.
                logger.LogWarning("Synchronisation BookStack échouée [{DiagnosticCode}], tâche {JobId}, type {FailureType}.",
                    diagnostic.Code, id, failure.GetType().Name);
            }
        }
    }
}
