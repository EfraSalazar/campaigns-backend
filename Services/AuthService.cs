using System.Security.Cryptography;
using System.Text;
using EventCampaignSystem.Data;
using EventCampaignSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EventCampaignSystem.Services;

public class AuthService
{
    private readonly CampaignDbContext _context;

    public AuthService(CampaignDbContext context)
    {
        _context = context;
    }

    public async Task<User?> AuthenticateAsync(string username, string password)
    {
        var user = await _context.Users
            .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

        if (user == null)
        {
            return null;
        }

        return VerifyPassword(password, user.PasswordHash, user.Salt) ? user : null;
    }

    private static string HashPassword(string password, string salt)
    {
        using var sha256 = SHA256.Create();
        var saltedPassword = string.Concat(password, salt);
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(saltedPassword));
        return Convert.ToBase64String(hashBytes);
    }

    private static bool VerifyPassword(string password, string storedHash, string storedSalt)
    {
        return HashPassword(password, storedSalt) == storedHash;
    }
}
