using EventCampaignSystem.Data;
using Microsoft.AspNetCore.Authorization;
using EventCampaignSystem.DTOs;
using EventCampaignSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventCampaignSystem.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public class ConsentsController : ControllerBase
{
    private readonly CampaignDbContext _context;

    public ConsentsController(CampaignDbContext context)
    {
        _context = context;
    }

    [HttpGet("contact/{contactId:int}")]
    public async Task<IActionResult> GetContactConsents(int contactId)
    {
        var consents = await _context.ContactConsents
            .Where(c => c.ContactId == contactId)
            .OrderBy(c => c.Purpose)
            .ThenBy(c => c.Channel)
            .Select(c => ToResponse(c))
            .ToListAsync();

        return Ok(consents);
    }

    [HttpPut("contact/{contactId:int}")]
    public async Task<IActionResult> UpsertConsent(int contactId, ConsentUpsertRequest request)
    {
        var contactExists = await _context.Contacts.AnyAsync(c => c.Id == contactId);
        if (!contactExists)
        {
            return NotFound(new { error = $"No se encontro el contacto {contactId}" });
        }

        var purpose = request.Purpose.Trim();
        var channel = request.Channel.Trim();

        if (string.IsNullOrWhiteSpace(purpose) || string.IsNullOrWhiteSpace(channel))
        {
            return BadRequest(new { error = "Purpose y Channel son obligatorios" });
        }

        var consent = await _context.ContactConsents
            .FirstOrDefaultAsync(c => c.ContactId == contactId && c.Purpose == purpose && c.Channel == channel);

        if (consent == null)
        {
            consent = new ContactConsent
            {
                ContactId = contactId,
                Purpose = purpose,
                Channel = channel
            };

            _context.ContactConsents.Add(consent);
        }

        consent.Accepted = request.Accepted;
        consent.Source = request.Source?.Trim() ?? string.Empty;
        consent.Notes = request.Notes;
        consent.AcceptedAt = request.Accepted ? DateTime.UtcNow : consent.AcceptedAt;
        consent.RevokedAt = request.Accepted ? null : DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(ToResponse(consent));
    }

    private static ConsentResponse ToResponse(ContactConsent consent)
    {
        return new ConsentResponse
        {
            Id = consent.Id,
            ContactId = consent.ContactId,
            Purpose = consent.Purpose,
            Channel = consent.Channel,
            Accepted = consent.Accepted,
            AcceptedAt = consent.AcceptedAt,
            RevokedAt = consent.RevokedAt,
            Source = consent.Source,
            Notes = consent.Notes
        };
    }
}
