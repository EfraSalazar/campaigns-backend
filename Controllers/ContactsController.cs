using EventCampaignSystem.Data;
using Microsoft.AspNetCore.Authorization;
using EventCampaignSystem.DTOs;
using EventCampaignSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventCampaignSystem.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public class ContactsController : ControllerBase
{
    private readonly CampaignDbContext _context;
    private readonly ContactQueryService _contactQueryService;

    public ContactsController(CampaignDbContext context, ContactQueryService contactQueryService)
    {
        _context = context;
        _contactQueryService = contactQueryService;
    }

    [HttpGet]
    public async Task<IActionResult> GetContacts([FromQuery] ContactFilterRequest filter)
    {
        filter.Limit = Math.Clamp(filter.Limit, 1, 500);
        filter.Offset = Math.Max(filter.Offset, 0);

        var query = _contactQueryService.BuildQuery(filter)
            .OrderBy(c => c.State)
            .ThenBy(c => c.City)
            .ThenBy(c => c.FirstName);

        var total = await query.CountAsync();
        var items = await ContactQueryService.ProjectToResponse(
                query.Skip(filter.Offset).Take(filter.Limit))
            .ToListAsync();

        return Ok(new
        {
            total,
            filter.Offset,
            filter.Limit,
            items
        });
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetContact(int id)
    {
        var contact = await _context.Contacts
            .Include(c => c.Registrations)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (contact == null)
        {
            return NotFound(new { error = $"No se encontro el contacto {id}" });
        }

        return Ok(ContactQueryService.ToResponse(contact));
    }

    [HttpGet("{id:int}/registrations")]
    public async Task<IActionResult> GetRegistrationHistory(int id)
    {
        var exists = await _context.Contacts.AnyAsync(c => c.Id == id);
        if (!exists)
        {
            return NotFound(new { error = $"No se encontro el contacto {id}" });
        }

        var registrations = await _context.Registrations
            .Include(r => r.Event)
            .Where(r => r.ContactId == id)
            .OrderByDescending(r => r.RegistrationDate)
            .Select(r => new RegistrationHistoryResponse
            {
                Id = r.Id,
                ReservationCode = r.ReservationCode,
                EventId = r.EventId,
                EventName = r.Event != null ? r.Event.Name : string.Empty,
                RegistrationDate = r.RegistrationDate,
                HasAttended = r.HasAttended
            })
            .ToListAsync();

        return Ok(registrations);
    }
}
