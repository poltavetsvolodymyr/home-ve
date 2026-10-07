using HomeBackend.Infrastructure;

namespace HomeBackend.Features.Logs;

/// <summary>Reads the systemd journal. No endpoints of its own: the VMs feature serves each VM's log.</summary>
public static class LogsFeature
{
    public static IServiceCollection AddLogsFeature(this IServiceCollection services, bool useMockData) =>
        services.AddDataSource<IJournalSource, JournalctlSource, MockJournalSource>(useMockData);
}
