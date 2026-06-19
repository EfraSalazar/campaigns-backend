using System.ComponentModel.DataAnnotations;

namespace EventCampaignSystem.Models;

public class ContactConsent
{
    [Key]
    public int Id { get; set; }

    public int ContactId { get; set; }

    [MaxLength(80)]
    public string Purpose { get; set; } = string.Empty;

    [MaxLength(40)]
    public string Channel { get; set; } = string.Empty;

    public bool Accepted { get; set; }

    public DateTime? AcceptedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    [MaxLength(120)]
    public string Source { get; set; } = string.Empty;

    public string? Notes { get; set; }

    public Contact Contact { get; set; } = null!;
}
