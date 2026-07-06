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

        // Verifica ambos esquemas (SHA256 heredado y PBKDF2). El re-hasheo a PBKDF2
        // lo realiza registration-backend al iniciar sesión; aquí solo se lee.
        return PasswordHasher.Verify(password, user.PasswordHash, user.Salt) ? user : null;
    }
}
