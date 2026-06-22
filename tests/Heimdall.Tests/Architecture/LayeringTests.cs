using System.Reflection;
using Heimdall.Application;
using Heimdall.Domain;
using NetArchTest.Rules;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Architecture;

/// <summary>Enforces the Clean Architecture dependency direction: Domain &lt;- Application &lt;- Infrastructure* &lt;- Api.</summary>
public class LayeringTests
{
    static readonly Assembly Domain = typeof(GatewayConfig).Assembly;
    static readonly Assembly Application = typeof(IConfigLoader).Assembly;
    static readonly Assembly Infrastructure = typeof(Heimdall.Infrastructure.DependencyInjection).Assembly;
    static readonly Assembly Loader = typeof(Heimdall.Infrastructure.XmlOpenApiLoader.DependencyInjection).Assembly;
    static readonly Assembly ApiOpsLoader = typeof(Heimdall.Infrastructure.ApiOpsLoader.DependencyInjection).Assembly;
    static readonly Assembly Api = typeof(Program).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_above_it()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot().HaveDependencyOnAny("Heimdall.Application", "Heimdall.Infrastructure", "Heimdall.Api")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Format(result));
    }

    [Fact]
    public void Application_depends_only_on_Domain()
    {
        var result = Types.InAssembly(Application)
            .ShouldNot().HaveDependencyOnAny("Heimdall.Infrastructure", "Heimdall.Api")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Format(result));
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_Api()
    {
        var result = Types.InAssembly(Infrastructure)
            .ShouldNot().HaveDependencyOn("Heimdall.Api")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Format(result));
    }

    [Fact]
    public void Loader_does_not_depend_on_Api()
    {
        var result = Types.InAssembly(Loader)
            .ShouldNot().HaveDependencyOn("Heimdall.Api")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Format(result));
    }

    [Fact]
    public void ApiOps_loader_does_not_depend_on_Api()
    {
        var result = Types.InAssembly(ApiOpsLoader)
            .ShouldNot().HaveDependencyOn("Heimdall.Api")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Format(result));
    }

    [Fact]
    public void Validators_live_in_Application()
    {
        var result = Types.InAssembly(Application)
            .That().HaveNameEndingWith("Validator")
            .Should().ResideInNamespace("Heimdall.Application")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Format(result));
    }

    [Fact]
    public void Policies_live_in_Infrastructure()
    {
        var result = Types.InAssembly(Infrastructure)
            .That().ImplementInterface(typeof(IPolicy))
            .Should().ResideInNamespaceStartingWith("Heimdall.Infrastructure.Policies")
            .GetResult();
        result.IsSuccessful.ShouldBeTrue(Format(result));
    }

    [Fact]
    public void Policies_do_not_live_in_the_Api_host()
    {
        // IPolicy implementations are pluggable infrastructure; the host must not define any.
        var policiesInApi = Types.InAssembly(Api)
            .That().ImplementInterface(typeof(IPolicy))
            .GetTypes();
        policiesInApi.ShouldBeEmpty();
    }

    static string Format(TestResult result) =>
        result.IsSuccessful ? "" : "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
