using System.Diagnostics.CodeAnalysis;

namespace HomeBackend.Infrastructure;

public static class DataSources
{
    /// <summary>
    /// Registers how a feature reads the host: the Linux implementation on the host,
    /// or the fake one when <c>HomeBackend:Mock</c> is on (development on Windows/macOS).
    /// </summary>
    public static IServiceCollection AddDataSource<TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TLinux,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TMock>(
        this IServiceCollection services, bool useMock)
        where TService : class
        where TLinux : class, TService
        where TMock : class, TService =>
        useMock ? services.AddSingleton<TService, TMock>() : services.AddSingleton<TService, TLinux>();
}
