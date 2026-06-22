namespace Heimdall.Domain;

/// <summary>A parsed policy document: the four pipeline sections, each a list of policy nodes.</summary>
public sealed record PolicyDocument(
    IReadOnlyList<PolicyNode> Inbound,
    IReadOnlyList<PolicyNode> Backend,
    IReadOnlyList<PolicyNode> Outbound,
    IReadOnlyList<PolicyNode> OnError);

/// <summary>One policy element, parsed but kept faithful: name, attributes, children, inner text.</summary>
public sealed record PolicyNode(
    string Name,
    IReadOnlyDictionary<string, string> Attributes,
    IReadOnlyList<PolicyNode> Children,
    string? RawText);
