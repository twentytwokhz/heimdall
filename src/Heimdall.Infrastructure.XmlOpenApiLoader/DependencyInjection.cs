using Heimdall.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.Infrastructure.XmlOpenApiLoader;

public static class DependencyInjection
{
    /// <summary>Registers the XML + OpenAPI config loader as the active <see cref="IConfigLoader"/>.</summary>
    public static IServiceCollection AddXmlOpenApiLoader(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IConfigLoader, XmlOpenApiConfigLoader>();
        return services;
    }
}
