using System.Net;
using System.Text;
using Heimdall.Api.Playground;
using Heimdall.Application;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Playground;

/// <summary>
/// The replay client assembles the outbound request from a <see cref="PlaygroundRequest"/>. A plain
/// body keeps the StringContent path (backward compatible); a structured <see cref="PlaygroundFormDataBody"/>
/// builds a multipart/form-data body (text parts inline, file parts decoded from base64). File handling
/// fails loud: bad base64 and oversized files are clear errors, never a silent drop.
/// </summary>
public class LoopbackReplayClientTests
{
    private static readonly Uri Gateway = new("http://localhost:5000");

    // Captures the request the client builds so the assembled content can be asserted.
    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Sent { get; private set; }

        public byte[]? SentBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Sent = request;
            SentBody = request.Content is null ? null : await request.Content.ReadAsByteArrayAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") };
        }
    }

    private static PlaygroundRequest Request(string? body, string? mediaType, PlaygroundFormDataBody? formData) =>
        new("Upload", "POST", "http://localhost:5000/catalog/items",
            "https://contoso.azure-api.net/catalog/items", [], body, mediaType, [], formData);

    [Fact]
    public async Task Assembles_multipart_with_text_and_file_parts()
    {
        var handler = new CapturingHandler();
        var client = new LoopbackReplayClient(new HttpClient(handler));

        var fileBytes = Encoding.UTF8.GetBytes("PNG-BYTES");
        var formData = new PlaygroundFormDataBody(
        [
            new PlaygroundFormField("title", TextValue: "Hi"),
            new PlaygroundFormField("file", FileBase64: Convert.ToBase64String(fileBytes)),
        ]);

        await client.ReplayAsync(Request(body: null, mediaType: null, formData), Gateway, default);

        handler.Sent!.Content!.Headers.ContentType!.MediaType.ShouldBe("multipart/form-data");
        handler.Sent.Content.Headers.ContentType.Parameters.ShouldContain(p => p.Name == "boundary");

        var sent = Encoding.UTF8.GetString(handler.SentBody!);
        // MultipartFormDataContent renders the part name in the Content-Disposition (quoting varies by
        // runtime, so match the name token, not a fixed quote style).
        sent.ShouldContain("name=title");
        sent.ShouldContain("Hi");
        sent.ShouldContain("name=file");
        // The decoded file bytes arrive in the multipart body.
        sent.ShouldContain("PNG-BYTES");
    }

    [Fact]
    public async Task Bad_base64_file_fails_loud()
    {
        var client = new LoopbackReplayClient(new HttpClient(new CapturingHandler()));
        var formData = new PlaygroundFormDataBody([new PlaygroundFormField("file", FileBase64: "!!! not base64 !!!")]);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => client.ReplayAsync(Request(body: null, mediaType: null, formData), Gateway, default));
        ex.Message.ShouldContain("file");
        ex.Message.ShouldContain("base64", Case.Insensitive);
    }

    [Fact]
    public async Task Oversized_file_is_rejected()
    {
        var client = new LoopbackReplayClient(new HttpClient(new CapturingHandler()));
        // 10 MB + 1 byte once decoded.
        var tooBig = new byte[10 * 1024 * 1024 + 1];
        var formData = new PlaygroundFormDataBody([new PlaygroundFormField("file", FileBase64: Convert.ToBase64String(tooBig))]);

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => client.ReplayAsync(Request(body: null, mediaType: null, formData), Gateway, default));
        ex.Message.ShouldContain("file");
        ex.Message.ShouldContain("10");
    }

    [Fact]
    public async Task Non_formdata_request_still_uses_the_body_path()
    {
        var handler = new CapturingHandler();
        var client = new LoopbackReplayClient(new HttpClient(handler));

        await client.ReplayAsync(
            Request(body: "{\"sku\":\"ACME-9\"}", mediaType: "application/json", formData: null), Gateway, default);

        handler.Sent!.Content!.Headers.ContentType!.MediaType.ShouldBe("application/json");
        Encoding.UTF8.GetString(handler.SentBody!).ShouldBe("{\"sku\":\"ACME-9\"}");
    }
}
