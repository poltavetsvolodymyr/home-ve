namespace HomeBackend.Features.Update;

/// <summary>Starts the self-update and reports on it.</summary>
public interface IUpdateRunner
{
    Task<UpdateStatus> ReadAsync(CancellationToken ct);

    /// <summary>Starts the update and returns at once; it goes on without the backend (which it restarts).</summary>
    Task StartAsync(CancellationToken ct);
}
