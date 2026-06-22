// Tiny stub backend for samples and local end-to-end runs. Fictional Acme data only.
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/catalog/items", () => Results.Json(new
{
    items = new[]
    {
        new { sku = "ACME-001", name = "Widget" },
        new { sku = "ACME-002", name = "Gadget" }
    }
}));

app.MapGet("/catalog/items/{id}", (string id) => Results.Json(new { sku = $"ACME-{id}", name = "Widget" }));

app.MapPost("/catalog/items", () => Results.Json(new { sku = "ACME-003", name = "Sprocket" }, statusCode: 201));

app.MapFallback(() => Results.Json(new { message = "Acme stub backend" }));

app.Run();
