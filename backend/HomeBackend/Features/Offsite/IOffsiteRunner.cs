namespace HomeBackend.Features.Offsite;

/// <summary>Starts the offsite upload and reports on it.</summary>
public interface IOffsiteRunner
{
    Task<OffsiteStatus> ReadAsync(CancellationToken ct);

    /// <summary>Starts an upload and returns at once; a running one is left alone.</summary>
    Task StartAsync(CancellationToken ct);
}
