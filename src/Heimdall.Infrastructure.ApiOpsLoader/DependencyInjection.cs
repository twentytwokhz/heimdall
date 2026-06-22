using Heimdall.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.Infrastructure.ApiOpsLoader;

public static class DependencyInjection
{
    /// <summary>Registers the APIOps extractor-folder loader as the active <see cref="IConfigLoader"/>.</summary>
    public static IServiceCollection AddApiOpsLoader(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IConfigLoader, ApiOpsConfigLoader>();
        return services;
    }
}
