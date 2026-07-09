using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace ProConnect.WebAPI.Helpers
{
    public static class JwtHelper
    {
        public static string GenerateToken(string userId, string email, string role, IConfiguration configuration)
        {
            var secretKey = configuration["JwtSettings:SecretKey"] ?? throw new Exception("JWT SecretKey not configured");
            var issuer = configuration["JwtSettings:Issuer"] ?? "ProConnect";
            var audience = configuration["JwtSettings:Audience"] ?? "ProConnectUsers";
            var expiryMinutes = int.Parse(configuration["JwtSettings:ExpiryMinutes"] ?? "60");

            var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

            var claims = new List<Claim>
            {
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(expiryMinutes),
                signingCredentials: credentials
            );

            return new JwtSecurityTokenHandler().WriteToken(token);
        }
    }
}