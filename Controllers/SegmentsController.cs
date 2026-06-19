using System.Text.Json;
using EventCampaignSystem.Data;
using EventCampaignSystem.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EventCampaignSystem.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public class SegmentsController : ControllerBase
{
    private readonly CampaignDbContext _context;

    public SegmentsController(CampaignDbContext context)
    {
        _context = context;
    }

    public class SaveSegmentRequest
    {
        public string Name { get; set; } = string.Empty;
        public JsonElement Filters { get; set; }
        public string SegmentBy { get; set; } = "none";
    }

    [HttpGet]
    public async Task<IActionResult> GetSegments()
    {
        var segments = await _context.SavedSegments
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync();

        return Ok(segments.Select(s => new
        {
            s.Id,
            s.Name,
            s.SegmentBy,
            s.CreatedBy,
            s.CreatedAt,
            filters = ParseFilters(s.FiltersJson)
        }));
    }

    [HttpPost]
    public async Task<IActionResult> SaveSegment(SaveSegmentRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "El nombre es obligatorio." });
        }

        var name = request.Name.Trim();
        var filtersJson = request.Filters.ValueKind == JsonValueKind.Undefined
            ? "{}"
            : request.Filters.GetRawText();

        var segment = await _context.SavedSegments.FirstOrDefaultAsync(s => s.Name == name);
        if (segment == null)
        {
            segment = new SavedSegment { Name = name, CreatedBy = User?.Identity?.Name ?? string.Empty };
            _context.SavedSegments.Add(segment);
        }

        segment.FiltersJson = filtersJson;
        segment.SegmentBy = string.IsNullOrWhiteSpace(request.SegmentBy) ? "none" : request.SegmentBy.Trim();

        await _context.SaveChangesAsync();

        return Ok(new
        {
            segment.Id,
            segment.Name,
            segment.SegmentBy,
            segment.CreatedBy,
            segment.CreatedAt,
            filters = ParseFilters(segment.FiltersJson)
        });
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteSegment(int id)
    {
        var segment = await _context.SavedSegments.FindAsync(id);
        if (segment == null)
        {
            return NotFound(new { error = "No se encontró el segmento." });
        }

        _context.SavedSegments.Remove(segment);
        await _context.SaveChangesAsync();
        return Ok(new { deleted = id });
    }

    private static object ParseFilters(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<JsonElement>(json);
        }
        catch
        {
            return new { };
        }
    }
}
