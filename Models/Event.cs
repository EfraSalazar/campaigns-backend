using System.ComponentModel.DataAnnotations;

namespace EventCampaignSystem.Models;

public class Event
{
    [Key]
    public int Id { get; set; }

    [MaxLength(120)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(80)]
    public string Slug { get; set; } = string.Empty;

    public int Year { get; set; }

    [MaxLength(100)]
    public string City { get; set; } = string.Empty;

    [MaxLength(100)]
    public string State { get; set; } = string.Empty;

    public DateTime? StartDate { get; set; }

    public DateTime? EndDate { get; set; }

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
}
