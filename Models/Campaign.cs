using System.ComponentModel.DataAnnotations;

namespace EventCampaignSystem.Models;

public class Campaign
{
    [Key]
    public int Id { get; set; }

    [MaxLength(160)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Purpose { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Channel { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Status { get; set; } = "Draft";

    [MaxLength(200)]
    public string? Subject { get; set; }

    public string MessageTemplate { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ScheduledAt { get; set; }

    public DateTime? SentAt { get; set; }

    [MaxLength(120)]
    public string CreatedBy { get; set; } = string.Empty;

    public ICollection<CampaignRecipient> Recipients { get; set; } = new List<CampaignRecipient>();
}
