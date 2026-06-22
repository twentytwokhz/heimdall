using System.Xml.Linq;
using Heimdall.Domain;

namespace Heimdall.Application;

/// <summary>
/// Serializes a <see cref="PolicyDocument"/> back to APIM policy XML: the inverse of
/// <see cref="PolicyXmlParser"/>. The config keeps parsed documents, not raw source, so the console's
/// policy editor renders the current policy by writing the live document out (loader-agnostic, no disk).
/// </summary>
public static class PolicyXmlWriter
{
    /// <summary>Writes the four sections as <c>&lt;policies&gt;</c> XML. A null document yields the empty skeleton.</summary>
    public static string Write(PolicyDocument? document) =>
        new XElement(
            "policies",
            Section("inbound", document?.Inbound),
            Section("backend", document?.Backend),
            Section("outbound", document?.Outbound),
            Section("on-error", document?.OnError)).ToString();

    private static XElement Section(string name, IReadOnlyList<PolicyNode>? nodes) =>
        new(name, (nodes ?? []).Select(ToElement));

    private static XElement ToElement(PolicyNode node)
    {
        var element = new XElement(node.Name);
        foreach (var (key, value) in node.Attributes)
        {
            element.SetAttributeValue(key, value);
        }

        // Mirror PolicyXmlParser.ToNode: a node has either children or inner text, never both.
        if (node.Children.Count > 0)
        {
            element.Add(node.Children.Select(ToElement));
        }
        else if (node.RawText is not null)
        {
            element.Value = node.RawText;
        }

        return element;
    }
}
