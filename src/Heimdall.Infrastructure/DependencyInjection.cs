using Heimdall.Application;
using Heimdall.Infrastructure.Expressions;
using Heimdall.Infrastructure.Policies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.Infrastructure;

public static class DependencyInjection
{
    /// <summary>Registers core infrastructure services: clock, stores, trace sink, expression engine, and the policy set.</summary>
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICounterStore, Stores.InMemoryCounterStore>();
        services.AddSingleton<ICacheStore, Stores.InMemoryCacheStore>();
        services.AddSingleton<ITraceSink, Tracing.InMemoryTraceSink>();
        // Stateless playground importers; the console picks one per upload via ICollectionImporter.CanImport.
        services.AddSingleton<ICollectionImporter, Playground.PostmanV21Importer>();
        services.AddSingleton<ICollectionImporter, Playground.HttpFileImporter>();
        services.AddExpressionEngine();
        services.AddPolicies();
        return services;
    }

    /// <summary>
    /// Scans this assembly for <see cref="IPolicy"/> implementations and builds the registry.
    /// Policies are stateless, so they are singletons; new policies are picked up by being defined here.
    /// </summary>
    public static IServiceCollection AddPolicies(this IServiceCollection services)
    {
        services.Scan(scan => scan
            .FromAssemblyOf<PolicyRegistry>()
            .AddClasses(c => c.AssignableTo<IPolicy>(), publicOnly: false)
            .As<IPolicy>()
            .WithSingletonLifetime());
        services.AddSingleton<IPolicyRegistry, PolicyRegistry>();
        return services;
    }

    /// <summary>Registers the Roslyn expression evaluator (holds the compile cache, so a singleton).</summary>
    public static IServiceCollection AddExpressionEngine(this IServiceCollection services)
    {
        services.AddSingleton<IExpressionEvaluator, RoslynExpressionEvaluator>();
        return services;
    }
}
