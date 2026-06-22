using Heimdall.Application;
using Heimdall.Infrastructure.Context;

namespace Heimdall.Api.Configuration;

/// <summary>
/// Warms the Roslyn compiler at host start by compiling a trivial expression, so the first real
/// request does not pay the cold-compile cost. Runs in the background to avoid delaying startup.
/// </summary>
internal sealed class ExpressionWarmupHostedService(
    IExpressionEvaluator evaluator,
    ILogger<ExpressionWarmupHostedService> logger) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = Task.Run(() =>
        {
            try
            {
                evaluator.Evaluate<bool>("@(true)", WarmupContext());
                logger.LogInformation("Expression engine warmed up.");
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Expression engine warm-up failed; the first request will pay the compile cost.");
            }
        }, cancellationToken);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private static IPolicyContext WarmupContext() => new PolicyContext
    {
        Request = new EmuRequest
        {
            Method = "GET",
            Url = new Uri("http://localhost/"),
            Headers = new Dictionary<string, string[]>(),
            Body = new HttpEmuBody("{}"),
        },
        Api = new ApiInfo("warmup", "warmup", ""),
        Operation = new OperationInfo("warmup", "GET", "/"),
    };
}
