using Heimdall.Application;
using Heimdall.Domain;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Loader;

public class PolicyXmlWriterTests
{
    [Fact]
    public void Roundtrips_sections_attributes_children_and_text()
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
        var original = PolicyXmlParser.Parse(xml);

        var roundtripped = PolicyXmlParser.Parse(PolicyXmlWriter.Write(original));

        AssertSameNodes(original.Inbound, roundtripped.Inbound);
        AssertSameNodes(original.Backend, roundtripped.Backend);
        AssertSameNodes(original.Outbound, roundtripped.Outbound);
        AssertSameNodes(original.OnError, roundtripped.OnError);
    }

    [Fact]
    public void Roundtrips_a_choose_branch_with_nested_policies()
    {
        const string xml = """
            <policies>
              <inbound>
                <choose>
                  <when condition="@(true)">
                    <set-header name="X-When" exists-action="override">
                      <value>1</value>
                    </set-header>
                  </when>
                  <otherwise>
                    <set-status code="403" />
                  </otherwise>
                </choose>
              </inbound>
            </policies>
            """;
        var original = PolicyXmlParser.Parse(xml);

        var roundtripped = PolicyXmlParser.Parse(PolicyXmlWriter.Write(original));

        AssertSameNodes(original.Inbound, roundtripped.Inbound);
    }

    [Fact]
    public void Writes_an_empty_skeleton_for_a_null_document()
    {
        var doc = PolicyXmlParser.Parse(PolicyXmlWriter.Write(null));

        doc.Inbound.ShouldBeEmpty();
        doc.Backend.ShouldBeEmpty();
        doc.Outbound.ShouldBeEmpty();
        doc.OnError.ShouldBeEmpty();
    }

    private static void AssertSameNodes(IReadOnlyList<PolicyNode> expected, IReadOnlyList<PolicyNode> actual)
    {
        actual.Count.ShouldBe(expected.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            var e = expected[i];
            var a = actual[i];
            a.Name.ShouldBe(e.Name);
            a.RawText.ShouldBe(e.RawText);
            a.Attributes.Count.ShouldBe(e.Attributes.Count);
            foreach (var (key, value) in e.Attributes)
            {
                a.Attributes[key].ShouldBe(value);
            }

            AssertSameNodes(e.Children, a.Children);
        }
    }
}
