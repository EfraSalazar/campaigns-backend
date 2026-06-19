using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EventCampaignSystem.Models;

public class Contact
{
    [Key]
    public int Id { get; set; }

    [MaxLength(150)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string SecondLastName { get; set; } = string.Empty;

    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    public int? Gender { get; set; }

    [Column(TypeName = "date")]
    public DateTime? BirthDate { get; set; }

    [MaxLength(150)]
    public string City { get; set; } = string.Empty;

    [MaxLength(150)]
    public string State { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Church { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<Registration> Registrations { get; set; } = new List<Registration>();
    public ICollection<CampaignRecipient> CampaignRecipients { get; set; } = new List<CampaignRecipient>();
    public ICollection<ContactConsent> Consents { get; set; } = new List<ContactConsent>();
}
