using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace Orchestra.Api.Auth;

public sealed class ApiKeyOptions
{
    public List<ApiKeyEntry> ApiKeys { get; set; } = new();
}

public sealed class ApiKeyEntry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";

    /// <summary>"admin" can do everything; "reader" can only read.</summary>
    public string Role { get; set; } = "reader";
}

public static class Roles
{
    public const string Admin = "admin";
    public const string Reader = "reader";
}

public static class Policies
{
    public const string Admin = "Admin";
}

/// <summary>Authenticates requests carrying an <c>X-Api-Key</c> header against the configured key list.</summary>
public sealed class ApiKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptions<ApiKeyOptions> keys)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var header) || string.IsNullOrWhiteSpace(header.ToString()))
            return Task.FromResult(AuthenticateResult.NoResult());

        var provided = Encoding.UTF8.GetBytes(header.ToString());

        // Compare against every key without short-circuiting on the first match, in constant time per key.
        ApiKeyEntry? match = null;
        foreach (var entry in keys.Value.ApiKeys)
        {
            var candidate = Encoding.UTF8.GetBytes(entry.Key);
            if (CryptographicOperations.FixedTimeEquals(candidate, provided) && entry.Key.Length > 0)
                match = entry;
        }

        if (match is null)
            return Task.FromResult(AuthenticateResult.Fail("Invalid API key."));

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, match.Name), new Claim(ClaimTypes.Role, match.Role.ToLowerInvariant())],
            Scheme);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
