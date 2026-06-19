using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EventCampaignSystem.Models;

public class Registration
{
    [Key]
    public int Id { get; set; }

    [MaxLength(100)]
    public string ReservationCode { get; set; } = string.Empty;

    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(150)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(150)]
    public string SecondLastName { get; set; } = string.Empty;

    public int Gender { get; set; }

    [Column(TypeName = "date")]
    public DateTime BirthDate { get; set; }

    [MaxLength(150)]
    public string City { get; set; } = string.Empty;

    [MaxLength(150)]
    public string State { get; set; } = string.Empty;

    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [MaxLength(250)]
    public string Church { get; set; } = string.Empty;

    public DateTime RegistrationDate { get; set; }

    public int EventId { get; set; }

    public int? ContactId { get; set; }

    public bool HasAttended { get; set; }

    public DateTime? AttendanceDateTime { get; set; }

    public int ParticipantTypeId { get; set; }

    public bool BuysFood { get; set; }

    public bool BuysShirt { get; set; }

    public bool RequiresLodging { get; set; }

    [MaxLength(10)]
    public string? ShirtSize { get; set; }

    public Contact? Contact { get; set; }
    public Event? Event { get; set; }
}
