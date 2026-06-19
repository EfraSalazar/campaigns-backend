using System.ComponentModel.DataAnnotations;

namespace EventCampaignSystem.Models;

public class SavedSegment
{
    [Key]
    public int Id { get; set; }

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    public string FiltersJson { get; set; } = "{}";

    [MaxLength(40)]
    public string SegmentBy { get; set; } = "none";

    [MaxLength(120)]
    public string CreatedBy { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
