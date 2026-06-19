using EventCampaignSystem.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventCampaignSystem.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly CampaignDbContext _context;

    public EventsController(CampaignDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetEvents()
    {
        var events = await _context.Events
            .OrderByDescending(e => e.Year)
            .ThenBy(e => e.Name)
            .Select(e => new
            {
                e.Id,
                e.Name,
                e.Slug,
                e.Year,
                e.City,
                e.State,
                e.IsActive,
                registrationCount = e.Registrations.Count
            })
            .ToListAsync();

        return Ok(events);
    }

    [HttpGet("active")]
    public async Task<IActionResult> GetActiveEvent()
    {
        var activeEvent = await _context.Events
            .OrderByDescending(e => e.Year)
            .ThenByDescending(e => e.Id)
            .FirstOrDefaultAsync(e => e.IsActive);

        if (activeEvent == null)
        {
            return NotFound(new { error = "No hay evento activo configurado" });
        }

        return Ok(activeEvent);
    }
}
