using System.ComponentModel.DataAnnotations;

namespace EventCampaignSystem.Models;

public class ContactTag
{
    [Key]
    public int Id { get; set; }

    [MaxLength(80)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(20)]
    public string Color { get; set; } = string.Empty;

    public ICollection<ContactTagAssignment> Assignments { get; set; } = new List<ContactTagAssignment>();
}
