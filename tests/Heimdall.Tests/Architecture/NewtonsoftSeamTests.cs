using System.Reflection;
using Heimdall.Application;
using Heimdall.Domain;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Architecture;

/// <summary>
/// Enforces the Newtonsoft seam: APIM exposes Newtonsoft to policy expressions, so it is referenced
/// only inside Heimdall.Infrastructure (the Roslyn ScriptOptions + EmuBody impl). It must never leak
/// into Domain, Application, the loader, or the Api host - those are System.Text.Json only.
/// </summary>
public class NewtonsoftSeamTests
{
    private const string Newtonsoft = "Newtonsoft.Json";

    private static readonly Assembly Domain = typeof(GatewayConfig).Assembly;
    private static readonly Assembly Application = typeof(IConfigLoader).Assembly;
    private static readonly Assembly Loader = typeof(Heimdall.Infrastructure.XmlOpenApiLoader.DependencyInjection).Assembly;
    private static readonly Assembly ApiOpsLoader = typeof(Heimdall.Infrastructure.ApiOpsLoader.DependencyInjection).Assembly;
    private static readonly Assembly Api = typeof(Program).Assembly;

    [Fact]
    public void Domain_has_no_newtonsoft_dependency() => AssertNoNewtonsoft(Domain);

    [Fact]
    public void Application_has_no_newtonsoft_dependency() => AssertNoNewtonsoft(Application);

    [Fact]
    public void Loader_has_no_newtonsoft_dependency() => AssertNoNewtonsoft(Loader);

    [Fact]
    public void ApiOps_loader_has_no_newtonsoft_dependency() => AssertNoNewtonsoft(ApiOpsLoader);

    [Fact]
    public void Api_has_no_newtonsoft_dependency() => AssertNoNewtonsoft(Api);

    private static void AssertNoNewtonsoft(Assembly assembly)
    {
        var result = Types.InAssembly(assembly)
            .ShouldNot().HaveDependencyOn(Newtonsoft)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(
            $"Newtonsoft leaked into {assembly.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}");
    }
}
