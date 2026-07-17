using Microsoft.Extensions.DependencyInjection;

namespace CadMax.Bridge.Core;

/// <summary>
/// Dependency-injection registration for the bridge core.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Register core dispatch services. Command handlers are opt-in and none are
    /// registered by the bootstrap host.
    /// </summary>
    public static IServiceCollection AddCadMaxBridgeCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddLogging();
        services.AddSingleton<CadCommandRegistry>(provider =>
            new CadCommandRegistry(provider.GetServices<ICadCommandHandler>()));
        services.AddSingleton<CadCommandDispatcher>();
        return services;
    }
}
