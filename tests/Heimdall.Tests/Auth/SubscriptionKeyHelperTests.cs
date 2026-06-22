using Heimdall.Api.Middleware;
using Heimdall.Application;
using Microsoft.AspNetCore.Http;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Auth;

public class SubscriptionKeyHelperTests
{
    [Fact]
    public void Extract_prefers_the_header_over_the_query()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers[SubscriptionKey.HeaderName] = "from-header";
        request.QueryString = QueryString.Create(SubscriptionKey.QueryName, "from-query");

        SubscriptionKey.Extract(request).ShouldBe("from-header");
    }

    [Fact]
    public void Extract_falls_back_to_the_query_when_no_header()
    {
        var request = new DefaultHttpContext().Request;
        request.QueryString = QueryString.Create(SubscriptionKey.QueryName, "from-query");

        SubscriptionKey.Extract(request).ShouldBe("from-query");
    }

    [Fact]
    public void Extract_returns_null_when_absent()
    {
        var request = new DefaultHttpContext().Request;

        SubscriptionKey.Extract(request).ShouldBeNull();
    }

    [Fact]
    public void Strip_removes_header_and_query_param_keeping_other_params()
    {
        var request = new DefaultHttpContext().Request;
        request.Headers[SubscriptionKey.HeaderName] = "secret";
        request.QueryString = QueryString.Empty.Add("page", "2").Add(SubscriptionKey.QueryName, "secret");

        SubscriptionKey.Strip(request);

        request.Headers.ContainsKey(SubscriptionKey.HeaderName).ShouldBeFalse();
        request.Query.ContainsKey(SubscriptionKey.QueryName).ShouldBeFalse();
        request.Query["page"].ToString().ShouldBe("2");
    }
}

public class ApimErrorShaperTests
{
    [Theory]
    [InlineData(
        SubscriptionKeyOutcome.MissingKey,
        """{"statusCode":401,"message":"Access denied due to missing subscription key. Make sure to include subscription key when making requests to an API."}""")]
    [InlineData(
        SubscriptionKeyOutcome.InvalidKey,
        """{"statusCode":401,"message":"Access denied due to invalid subscription key. Make sure to provide a valid key for an active subscription."}""")]
    public async Task Writes_the_apim_401_body_verbatim(SubscriptionKeyOutcome outcome, string expected)
    {
        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();

        await ApimErrorShaper.WriteUnauthorizedAsync(http.Response, outcome, CancellationToken.None);

        http.Response.StatusCode.ShouldBe(401);
        http.Response.ContentType.ShouldBe("application/json");
        http.Response.Body.Position = 0;
        var body = await new StreamReader(http.Response.Body).ReadToEndAsync();
        body.ShouldBe(expected);
    }
}
