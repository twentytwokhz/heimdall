using Heimdall.Application;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Loader;

public class PolicyXmlParserTests
{
    [Fact]
    public void Parses_sections_attributes_children_and_text()
    {
        const string xml = """
            <policies>
              <inbound>
                <base />
                <set-header name="x" exists-action="override">
                  <value>y</value>
                </set-header>
              </inbound>
              <backend><base /></backend>
              <outbound><base /></outbound>
              <on-error><base /></on-error>
            </policies>
            """;

        var doc = PolicyXmlParser.Parse(xml);

        doc.Inbound.Count.ShouldBe(2);
        doc.Inbound[0].Name.ShouldBe("base");

        var setHeader = doc.Inbound[1];
        setHeader.Name.ShouldBe("set-header");
        setHeader.Attributes["name"].ShouldBe("x");
        setHeader.Attributes["exists-action"].ShouldBe("override");
        setHeader.Children.Count.ShouldBe(1);
        setHeader.Children[0].Name.ShouldBe("value");
        setHeader.Children[0].RawText.ShouldBe("y");

        doc.OnError.Count.ShouldBe(1);
        doc.OnError[0].Name.ShouldBe("base");
    }

    [Fact]
    public void Missing_sections_yield_empty_lists()
    {
        var doc = PolicyXmlParser.Parse("<policies><inbound><base /></inbound></policies>");

        doc.Inbound.Count.ShouldBe(1);
        doc.Backend.ShouldBeEmpty();
        doc.Outbound.ShouldBeEmpty();
        doc.OnError.ShouldBeEmpty();
    }
}
