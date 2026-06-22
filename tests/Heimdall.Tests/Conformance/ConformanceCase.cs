using System.Globalization;
using System.Xml.Linq;
using Heimdall.Application;
using Heimdall.Domain;

namespace Heimdall.Tests.Conformance;

/// <summary>
/// One declarative conformance row: a policy + a request (+ optional backend stub) and the response
/// Heimdall must produce, asserting parity with documented APIM semantics. Loaded from Cases/*.xml.
/// </summary>
internal sealed record ConformanceCase(
    string Name,
    PolicyDocument Policy,
    IReadOnlyList<NamedValue> NamedValues,
    ConformanceRequest Request,
    BackendStub? Backend,
    ConformanceExpect Expect)
{
    public static ConformanceCase Load(string path)
    {
        var root = XDocument.Load(path).Root ?? throw new InvalidOperationException($"Empty case: {path}");
        var policiesElement = root.Element("policies") ?? throw new InvalidOperationException($"Case {path} has no <policies>.");

        var request = root.Element("request") ?? throw new InvalidOperationException($"Case {path} has no <request>.");
        var expect = root.Element("expect") ?? throw new InvalidOperationException($"Case {path} has no <expect>.");

        return new ConformanceCase(
            root.Attribute("name")?.Value ?? Path.GetFileNameWithoutExtension(path),
            PolicyXmlParser.Parse(policiesElement.ToString()),
            root.Elements("named-value")
                .Select(n => new NamedValue(n.Attribute("name")!.Value, n.Attribute("value")!.Value, Secret: false))
                .ToArray(),
            new ConformanceRequest(
                request.Attribute("method")?.Value ?? "GET",
                request.Attribute("path")?.Value ?? "/catalog/items",
                request.Attribute("body")?.Value,
                Int(request, "repeat") ?? 1,
                request.Elements("header")
                    .Select(h => new KeyValuePair<string, string>(h.Attribute("name")!.Value, h.Attribute("value")!.Value))
                    .ToArray()),
            root.Element("backend") is { } b
                ? new BackendStub(Int(b, "status") ?? 200, b.Attribute("body")?.Value)
                : null,
            new ConformanceExpect(
                Int(expect, "status"),
                expect.Elements("body-contains").Select(e => e.Value).ToArray(),
                expect.Elements("backend-body-contains").Select(e => e.Value).ToArray(),
                expect.Elements("header")
                    .Select(h => new HeaderExpect(h.Attribute("name")!.Value, h.Attribute("contains")?.Value))
                    .ToArray(),
                expect.Elements("backend-header")
                    .Select(h => new HeaderExpect(h.Attribute("name")!.Value, h.Attribute("contains")?.Value))
                    .ToArray()));
    }

    private static int? Int(XElement element, string name) =>
        int.TryParse(element.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : null;
}

internal sealed record ConformanceRequest(
    string Method, string Path, string? Body, int Repeat, IReadOnlyList<KeyValuePair<string, string>> Headers);

internal sealed record BackendStub(int Status, string? Body);

internal sealed record ConformanceExpect(
    int? Status,
    IReadOnlyList<string> BodyContains,
    IReadOnlyList<string> BackendBodyContains,
    IReadOnlyList<HeaderExpect> Headers,
    IReadOnlyList<HeaderExpect> BackendHeaders);

internal sealed record HeaderExpect(string Name, string? Contains);
