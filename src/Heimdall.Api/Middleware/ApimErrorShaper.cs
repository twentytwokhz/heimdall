using System.Text.Json;
using Heimdall.Application;
using Microsoft.AspNetCore.Http;

namespace Heimdall.Api.Middleware;

/// <summary>Writes APIM's verbatim error bodies so emulator responses match the real gateway byte for byte.</summary>
public static class ApimErrorShaper
{
    // The exact strings Azure API Management returns for the two subscription-key 401 cases.
    private const string MissingKeyMessage =
        "Access denied due to missing subscription key. Make sure to include subscription key when making requests to an API.";
    private const string InvalidKeyMessage =
        "Access denied due to invalid subscription key. Make sure to provide a valid key for an active subscription.";

    // Web defaults give camelCase ("statusCode"/"message") and compact output, matching APIM's body.
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Writes the APIM 401 body for a missing or invalid subscription key.</summary>
    public static async Task WriteUnauthorizedAsync(HttpResponse response, SubscriptionKeyOutcome outcome, CancellationToken ct)
    {
        if (response.HasStarted)
        {
            return;
        }

        var message = outcome switch
        {
            SubscriptionKeyOutcome.MissingKey => MissingKeyMessage,
            SubscriptionKeyOutcome.InvalidKey => InvalidKeyMessage,
            _ => throw new ArgumentOutOfRangeException(
                nameof(outcome), outcome, "Only MissingKey and InvalidKey map to a 401."),
        };

        response.Clear();
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.ContentType = "application/json";
        await response.WriteAsync(JsonSerializer.Serialize(new ApimError(401, message), SerializerOptions), ct);
    }

    private sealed record ApimError(int StatusCode, string Message);
}
