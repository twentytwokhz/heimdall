using Xunit;

namespace Heimdall.Tests.Fixtures;

/// <summary>
/// Groups the WebApplicationFactory&lt;Program&gt; based e2e tests so they run sequentially.
/// Concurrent host resolution races in HostFactoryResolver ("entry point exited without ever building an IHost").
/// </summary>
[CollectionDefinition("gateway-e2e")]
public sealed class GatewayE2eCollection;
