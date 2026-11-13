using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using MarksApi.Data;
using MarksApi.Data.Entities;
using MarksApi.Dtos;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
namespace MarksApi.Services;


public class AuthService : IAuthService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AuthService> _logger;
    public AuthService(AppDbContext db, IConfiguration configuration, ILogger<AuthService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }
    public async Task<LoginResponse?> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var user = await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(row => row.Username == username, cancellationToken);
        var hasher = new PasswordHasher<User>();
        if (user is null || hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Login failed for {Username}", username);
            return null;
        }
        var expiryMinutes = _configuration.GetValue("Jwt:ExpiryMinutes", 60);
        var expiresAtUtc = DateTime.UtcNow.AddMinutes(expiryMinutes);
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, user.Username),
            new(ClaimTypes.Role, user.Role.ToString())
        };
        if (user.StudentId is int studentId)
        {
            claims.Add(new Claim("studentId", studentId.ToString()));
        }
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_configuration["Jwt:Key"]!));
        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: expiresAtUtc,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new LoginResponse
        {
            Token = new JwtSecurityTokenHandler().WriteToken(token),
            ExpiresAtUtc = expiresAtUtc,
            Role = user.Role.ToString()
        };
    }
}