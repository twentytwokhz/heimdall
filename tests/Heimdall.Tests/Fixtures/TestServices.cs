using Heimdall.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Heimdall.Tests.Fixtures;

/// <summary>A DI container wired with the real infrastructure registrations, for policies that resolve the registry.</summary>
public static class TestServices
{
    public static ServiceProvider Policies() => new ServiceCollection()
        .AddLogging()
        .AddInfrastructure(new ConfigurationBuilder().Build())
        .BuildServiceProvider();
}
