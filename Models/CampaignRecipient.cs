using System.ComponentModel.DataAnnotations;

namespace EventCampaignSystem.Models;

public class CampaignRecipient
{
    [Key]
    public int Id { get; set; }

    public int CampaignId { get; set; }

    public int ContactId { get; set; }

    public int? SourceEventId { get; set; }

    [MaxLength(320)]
    public string RecipientAddress { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Status { get; set; } = "Pending";

    [MaxLength(120)]
    public string? ProviderMessageId { get; set; }

    public string? ErrorMessage { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? SentAt { get; set; }

    public Campaign Campaign { get; set; } = null!;
    public Contact Contact { get; set; } = null!;
    public Event? SourceEvent { get; set; }
}
