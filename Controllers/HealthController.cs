using EventCampaignSystem.Data;
using EventCampaignSystem.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventCampaignSystem.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthController : ControllerBase
{
    private readonly CampaignDbContext _context;
    private readonly SendingSettings _sending;

    public HealthController(CampaignDbContext context, IOptions<SendingSettings> sending)
    {
        _context = context;
        _sending = sending.Value;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var canConnect = await _context.Database.CanConnectAsync();
        return Ok(new
        {
            service = "EventCampaignSystem",
            database = canConnect ? "ok" : "unavailable",
            testMode = _sending.TestMode,
            testEmail = _sending.TestMode ? _sending.TestEmail : null,
            testPhone = _sending.TestMode ? _sending.TestPhone : null,
            timestamp = DateTime.UtcNow
        });
    }
}
