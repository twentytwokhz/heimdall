using System.Text;
using Heimdall.Application;
using Heimdall.Domain;
using Heimdall.Infrastructure.Context;
using Heimdall.Infrastructure.Policies.Auth;
using Heimdall.Tests.Fixtures;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Shouldly;
using Xunit;

namespace Heimdall.Tests.Policies;

public class ValidateJwtPolicyTests
{
    private const string Secret = "heimdall-test-signing-secret-of-32+bytes!!";
    private static readonly string SecretBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(Secret));

    private static string CreateToken(DateTime? expires = null, string secret = Secret, IDictionary<string, object>? claims = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var exp = expires ?? DateTime.UtcNow.AddMinutes(5);
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            // Anchor nbf/iat safely before exp so a deliberately-expired token is still well-formed.
            NotBefore = exp.AddMinutes(-10),
            IssuedAt = exp.AddMinutes(-10),
            Expires = exp,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256),
            Claims = claims ?? new Dictionary<string, object> { ["sub"] = "alice" },
        });
    }

    private static PolicyNode Node(string keyText) => Node([keyText], null, []);

    private static PolicyNode Node(
        IEnumerable<string> keys,
        IReadOnlyDictionary<string, string>? attributes,
        IReadOnlyList<PolicyNode> extraChildren)
    {
        var attrs = attributes ?? new Dictionary<string, string> { ["header-name"] = "Authorization" };
        var keyNodes = keys.Select(k => new PolicyNode("key", new Dictionary<string, string>(), [], k)).ToArray();
        var children = new List<PolicyNode>
        {
            new("issuer-signing-keys", new Dictionary<string, string>(), keyNodes, null),
        };
        children.AddRange(extraChildren);
        return new PolicyNode("validate-jwt", attrs, children, null);
    }

    private static PolicyNode RequiredClaim(string name, string match, params string[] values)
    {
        var valueNodes = values.Select(v => new PolicyNode("value", new Dictionary<string, string>(), [], v)).ToArray();
        var claim = new PolicyNode("claim", new Dictionary<string, string> { ["name"] = name, ["match"] = match }, valueNodes, null);
        return new PolicyNode("required-claims", new Dictionary<string, string>(), [claim], null);
    }

    private static async Task<PolicyContext> RunAsync(string keyText, string? authHeader)
    {
        var ctx = PolicyContexts.For(PolicySection.Inbound);
        if (authHeader is not null)
        {
            ctx.Request.Headers["Authorization"] = [authHeader];
        }
        await new ValidateJwtPolicy().ApplyAsync(ctx, Node(keyText));
        return ctx;
    }

    [Fact]
    public async Task Token_from_query_parameter_passes()
    {
        var ctx = PolicyContexts.For(PolicySection.Inbound, url: $"http://localhost/catalog/items?token={CreateToken()}");
        var node = Node([SecretBase64], new Dictionary<string, string> { ["query-parameter-name"] = "token" }, []);

        await new ValidateJwtPolicy().ApplyAsync(ctx, node);

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Token_query_parameter_name_is_matched_case_insensitively()
    {
        // APIM treats query parameter names case-insensitively; "?Token=" must satisfy query-parameter-name="token".
        var ctx = PolicyContexts.For(PolicySection.Inbound, url: $"http://localhost/catalog/items?Token={CreateToken()}");
        var node = Node([SecretBase64], new Dictionary<string, string> { ["query-parameter-name"] = "token" }, []);

        await new ValidateJwtPolicy().ApplyAsync(ctx, node);

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Clock_skew_tolerates_a_recently_expired_token()
    {
        var ctx = PolicyContexts.For(PolicySection.Inbound);
        ctx.Request.Headers["Authorization"] = [$"Bearer {CreateToken(expires: DateTime.UtcNow.AddSeconds(-30))}"];
        var node = Node([SecretBase64], new Dictionary<string, string> { ["clock-skew"] = "120" }, []);

        await new ValidateJwtPolicy().ApplyAsync(ctx, node);

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Require_scheme_rejects_a_token_with_the_wrong_scheme()
    {
        // A bare token (no scheme prefix) is otherwise valid; require-scheme="Bearer" must reject it.
        var ctx = PolicyContexts.For(PolicySection.Inbound);
        ctx.Request.Headers["Authorization"] = [CreateToken()];
        var node = Node([SecretBase64], new Dictionary<string, string> { ["require-scheme"] = "Bearer" }, []);

        await new ValidateJwtPolicy().ApplyAsync(ctx, node);

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task Require_scheme_accepts_the_matching_scheme()
    {
        var ctx = PolicyContexts.For(PolicySection.Inbound);
        ctx.Request.Headers["Authorization"] = [$"Bearer {CreateToken()}"];
        var node = Node([SecretBase64], new Dictionary<string, string> { ["require-scheme"] = "Bearer" }, []);

        await new ValidateJwtPolicy().ApplyAsync(ctx, node);

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Required_claims_match_all_rejects_when_a_value_is_absent()
    {
        var token = CreateToken(claims: new Dictionary<string, object> { ["sub"] = "alice", ["role"] = new[] { "admin" } });
        var ctx = PolicyContexts.For(PolicySection.Inbound);
        ctx.Request.Headers["Authorization"] = [$"Bearer {token}"];
        var node = Node([SecretBase64], null, [RequiredClaim("role", "all", "admin", "editor")]);

        await new ValidateJwtPolicy().ApplyAsync(ctx, node);

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task Required_claims_match_any_passes_when_one_value_is_present()
    {
        var token = CreateToken(claims: new Dictionary<string, object> { ["sub"] = "alice", ["role"] = new[] { "admin" } });
        var ctx = PolicyContexts.For(PolicySection.Inbound);
        ctx.Request.Headers["Authorization"] = [$"Bearer {token}"];
        var node = Node([SecretBase64], null, [RequiredClaim("role", "any", "editor", "admin")]);

        await new ValidateJwtPolicy().ApplyAsync(ctx, node);

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Multiple_keys_one_matching_passes()
    {
        var wrongKey = Convert.ToBase64String(Encoding.UTF8.GetBytes("a-different-signing-secret-of-32+bytes!!"));
        var ctx = PolicyContexts.For(PolicySection.Inbound);
        ctx.Request.Headers["Authorization"] = [$"Bearer {CreateToken()}"];
        var node = Node([wrongKey, SecretBase64], null, []);

        await new ValidateJwtPolicy().ApplyAsync(ctx, node);

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Valid_token_passes()
    {
        var ctx = await RunAsync(SecretBase64, $"Bearer {CreateToken()}");

        ctx.ShortCircuited.ShouldBeFalse();
    }

    [Fact]
    public async Task Missing_token_is_rejected_with_401()
    {
        var ctx = await RunAsync(SecretBase64, authHeader: null);

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task Token_with_a_bad_signature_is_rejected()
    {
        var ctx = await RunAsync(SecretBase64, $"Bearer {CreateToken(secret: "a-different-signing-secret-of-32+bytes!!")}");

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(401);
    }

    [Fact]
    public async Task Expired_token_is_rejected()
    {
        var ctx = await RunAsync(SecretBase64, $"Bearer {CreateToken(expires: DateTime.UtcNow.AddMinutes(-10))}");

        ctx.ShortCircuited.ShouldBeTrue();
        ctx.Response.StatusCode.ShouldBe(401);
    }
}
