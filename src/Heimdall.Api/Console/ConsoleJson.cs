using System.Text.Json;
using System.Text.Json.Serialization;

namespace Heimdall.Api.Console;

/// <summary>
/// The serializer options shared by the console REST endpoints and the SignalR hub protocol, so a
/// trace streamed over the hub is byte-identical to one fetched from <c>/_apim/traces</c>.
/// Web defaults give camelCase; the enum converter renders <c>TraceOutcome</c> etc. as strings.
/// </summary>
internal static class ConsoleJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };
}
