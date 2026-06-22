using System.Xml.Linq;
using Heimdall.Domain;

namespace Heimdall.Application;

/// <summary>
/// Parses APIM policy XML into a faithful <see cref="PolicyDocument"/> of <see cref="PolicyNode"/>s.
/// Lives in Application (engine layer) because APIM policy XML is the canonical policy format every
/// config loader produces - loaders depend only on Application, so the shared parser belongs here.
/// </summary>
public static class PolicyXmlParser
{
    public static PolicyDocument Parse(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        return new PolicyDocument(
            ParseSection(root, "inbound"),
            ParseSection(root, "backend"),
            ParseSection(root, "outbound"),
            ParseSection(root, "on-error"));
    }

    /// <summary>Parses a policy fragment file (a <c>&lt;fragment&gt;</c> root of policy elements) into its nodes.</summary>
    public static IReadOnlyList<PolicyNode> ParseFragment(string xml)
    {
        var root = XDocument.Parse(xml).Root;
        return root is null ? [] : root.Elements().Select(ToNode).ToList();
    }

    private static IReadOnlyList<PolicyNode> ParseSection(XElement? root, string sectionName)
    {
        var section = root?.Element(sectionName);
        return section is null ? [] : section.Elements().Select(ToNode).ToList();
    }

    private static PolicyNode ToNode(XElement element) =>
        new(
            element.Name.LocalName,
            element.Attributes().ToDictionary(a => a.Name.LocalName, a => a.Value),
            element.Elements().Select(ToNode).ToList(),
            element.HasElements || string.IsNullOrWhiteSpace(element.Value) ? null : element.Value);
}
