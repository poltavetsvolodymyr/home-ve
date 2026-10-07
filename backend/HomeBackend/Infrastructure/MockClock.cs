namespace HomeBackend.Infrastructure;

/// <summary>Fake boot time shared by the mock data sources, so uptimes on different pages agree.</summary>
public static class MockClock
{
    public static readonly DateTimeOffset BootTime = DateTimeOffset.UtcNow.AddDays(-12.3);
}
