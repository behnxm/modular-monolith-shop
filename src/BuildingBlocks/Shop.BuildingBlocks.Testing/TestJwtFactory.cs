using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Shop.BuildingBlocks.Testing;

public sealed class TestJwtFactory
{
    private readonly ECDsa _ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly ECDsaSecurityKey _securityKey;

    public TestJwtFactory()
    {
        _securityKey = new ECDsaSecurityKey(_ecdsa) { KeyId = "test-signing-key" };
    }

    public const string Issuer = "https://auth.shop.local";
    public const string Audience = "shop-api";
    public SecurityKey SecurityKey => _securityKey;

    public string CreateToken(
        string userId,
        string? displayName = null,
        IEnumerable<string>? scopes = null,
        TimeSpan? lifetime = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        var claims = new List<Claim>
        {
            new("sub", userId),
            new("name", displayName ?? userId),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (scopes is not null)
        {
            var scopeList = scopes.ToList();
            if (scopeList.Count > 0)
            {
                // Both standard space-delimited scope and individual scope claims
                claims.Add(new Claim("scope", string.Join(" ", scopeList)));
                foreach (var scope in scopeList)
                {
                    claims.Add(new Claim("scope", scope));
                }
            }
        }

        var expires = DateTime.UtcNow.Add(lifetime ?? TimeSpan.FromHours(1));
        var credentials = new SigningCredentials(_securityKey, SecurityAlgorithms.EcdsaSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = expires,
            SigningCredentials = credentials
        };

        var handler = new JwtSecurityTokenHandler();
        var token = handler.CreateToken(tokenDescriptor);
        return handler.WriteToken(token);
    }

    public string CreateCustomerToken(string userId = "cust_01", string displayName = "Alice Customer") =>
        CreateToken(userId, displayName, ["shop.customer"]);

    public string CreateOtherCustomerToken(string userId = "cust_02", string displayName = "Bob OtherCustomer") =>
        CreateToken(userId, displayName, ["shop.customer"]);

    public string CreateAdminToken(string userId = "admin_01", string displayName = "Carol Admin") =>
        CreateToken(userId, displayName, ["shop.admin", "shop.customer"]);
}
