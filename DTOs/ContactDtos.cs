namespace EventCampaignSystem.DTOs;

public class ContactFilterRequest
{
    public string? Search { get; set; }
    public string? City { get; set; }
    public string? State { get; set; }
    public string? Church { get; set; }
    public int? EventId { get; set; }
    public string? ConsentPurpose { get; set; }
    public string? ConsentChannel { get; set; }
    public bool RequireConsent { get; set; }
    public int Limit { get; set; } = 100;
    public int Offset { get; set; } = 0;
}

public class ContactResponse
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Church { get; set; } = string.Empty;
    public int RegistrationCount { get; set; }
    public DateTime? LastRegistrationDate { get; set; }
}

public class RegistrationHistoryResponse
{
    public int Id { get; set; }
    public string ReservationCode { get; set; } = string.Empty;
    public int EventId { get; set; }
    public string EventName { get; set; } = string.Empty;
    public DateTime RegistrationDate { get; set; }
    public bool HasAttended { get; set; }
}
