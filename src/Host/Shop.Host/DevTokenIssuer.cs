using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;

namespace Shop.Host;

public sealed record DevTokenRequest(
    [property: JsonPropertyName("userId")] string UserId,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("scopes")] string[]? Scopes);

public static class DevTokenIssuer
{
    public const string DevIssuerAuthority = "https://auth.shop.local";
    public const string DevKeyId = "dev-issuer-key-2026";

    private static ECDsa? _devKey;
    private static ECDsaSecurityKey? _securityKey;

    public static ECDsaSecurityKey GetOrLoadSigningKey(IConfiguration configuration, IHostEnvironment environment)
    {
        if (_securityKey is not null)
        {
            return _securityKey;
        }

        var keyPath = configuration["DevIssuer:SigningKeyPath"] ?? "secrets/dev-issuer.key";
        if (File.Exists(keyPath))
        {
            var pem = File.ReadAllText(keyPath);
            _devKey = ECDsa.Create();
            _devKey.ImportFromPem(pem);
        }
        else
        {
            _devKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            if (!environment.IsProduction())
            {
                var dir = Path.GetDirectoryName(keyPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }
                File.WriteAllText(keyPath, _devKey.ExportECPrivateKeyPem());
            }
        }

        _securityKey = new ECDsaSecurityKey(_devKey) { KeyId = DevKeyId };
        return _securityKey;
    }

    public static void ValidateConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var enabled = configuration.GetValue<bool>("DevIssuer:Enabled");
        if (!enabled)
        {
            return;
        }

        if (environment.IsProduction())
        {
            throw new InvalidOperationException(
                "FATAL SECURITY VIOLATION: DevIssuer must NEVER be enabled in Production environment!");
        }

        var authority = configuration["Authentication:Authority"];
        if (!string.IsNullOrWhiteSpace(authority) && authority != DevIssuerAuthority)
        {
            throw new InvalidOperationException(
                "FATAL CONFIGURATION ERROR: DevIssuer cannot be enabled together with external Authentication:Authority.");
        }
    }

    public static void MapDevEndpoints(IEndpointRouteBuilder endpoints, IConfiguration configuration, IHostEnvironment environment)
    {
        var enabled = configuration.GetValue<bool>("DevIssuer:Enabled");
        if (!enabled)
        {
            return;
        }

        var signingKey = GetOrLoadSigningKey(configuration, environment);

        endpoints.MapPost("/dev/token", HandleDevToken)
        .AllowAnonymous()
        .RequireRateLimiting("dev-token")
        .WithTags("Development")
        .WithSummary("Issue a short-lived development bearer token (NON-PRODUCTION ONLY)");

        endpoints.MapPost("/dev-token", HandleDevToken)
        .AllowAnonymous()
        .RequireRateLimiting("dev-token")
        .WithTags("Development")
        .WithSummary("Issue a short-lived development bearer token (NON-PRODUCTION ONLY)");

        endpoints.MapGet("/.well-known/jwks.json", () =>
        {
            var parameters = _devKey!.ExportParameters(false);
            var jwk = new JsonWebKey
            {
                Kty = JsonWebAlgorithmsKeyTypes.EllipticCurve,
                Crv = JsonWebKeyECTypes.P256,
                Kid = DevKeyId,
                Use = "sig",
                Alg = SecurityAlgorithms.EcdsaSha256,
                X = Base64UrlEncoder.Encode(parameters.Q.X!),
                Y = Base64UrlEncoder.Encode(parameters.Q.Y!)
            };

            var jwks = new JsonWebKeySet();
            jwks.Keys.Add(jwk);

            return Results.Ok(jwks);
        })
        .AllowAnonymous()
        .WithTags("Development");

        IResult HandleDevToken(DevTokenRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.UserId))
            {
                return Results.BadRequest(new { error = "userId is required" });
            }

            var claims = new List<Claim>
            {
                new("sub", request.UserId),
                new("name", request.DisplayName ?? request.UserId),
                new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            if (request.Scopes is { Length: > 0 })
            {
                claims.Add(new Claim("scope", string.Join(" ", request.Scopes)));
                foreach (var scope in request.Scopes)
                {
                    claims.Add(new Claim("scope", scope));
                }
            }

            var expires = DateTime.UtcNow.AddHours(2);
            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Issuer = DevIssuerAuthority,
                Audience = configuration["Authentication:Audience"] ?? "shop-api",
                Subject = new ClaimsIdentity(claims),
                Expires = expires,
                SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.EcdsaSha256)
            };

            var handler = new JwtSecurityTokenHandler();
            var token = handler.CreateToken(tokenDescriptor);
            var tokenString = handler.WriteToken(token);

            return Results.Ok(new
            {
                token = tokenString,
                tokenType = "Bearer",
                expiresIn = 7200
            });
        }
    }
}
