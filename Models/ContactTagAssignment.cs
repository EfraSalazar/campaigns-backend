namespace EventCampaignSystem.Models;

public class ContactTagAssignment
{
    public int ContactId { get; set; }

    public int ContactTagId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Contact Contact { get; set; } = null!;
    public ContactTag ContactTag { get; set; } = null!;
}
