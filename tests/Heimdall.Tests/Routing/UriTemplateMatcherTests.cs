using Heimdall.Api.Routing;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Routing;

public class UriTemplateMatcherTests
{
    [Fact]
    public void Literal_template_matches_with_no_values()
    {
        var matched = UriTemplateMatcher.TryMatch("/catalog/items", "/catalog/items", out var values);

        matched.ShouldBeTrue();
        values.ShouldBeEmpty();
    }

    [Fact]
    public void Template_with_parameter_captures_value()
    {
        var matched = UriTemplateMatcher.TryMatch("/catalog/items/{id}", "/catalog/items/42", out var values);

        matched.ShouldBeTrue();
        values["id"].ShouldBe("42");
    }

    [Fact]
    public void Fewer_segments_than_template_does_not_match()
    {
        var matched = UriTemplateMatcher.TryMatch("/catalog/items/{id}", "/catalog/items", out _);

        matched.ShouldBeFalse();
    }

    [Fact]
    public void More_segments_than_template_does_not_match()
    {
        var matched = UriTemplateMatcher.TryMatch("/catalog/items/{id}", "/catalog/items/42/extra", out _);

        matched.ShouldBeFalse();
    }

    [Fact]
    public void Trailing_slash_is_tolerated()
    {
        var matched = UriTemplateMatcher.TryMatch("/catalog/items", "/catalog/items/", out var values);

        matched.ShouldBeTrue();
        values.ShouldBeEmpty();
    }
}
