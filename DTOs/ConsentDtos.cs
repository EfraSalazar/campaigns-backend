namespace EventCampaignSystem.DTOs;

public class ConsentUpsertRequest
{
    public string Purpose { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Notes { get; set; }
}

public class ConsentResponse
{
    public int Id { get; set; }
    public int ContactId { get; set; }
    public string Purpose { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public bool Accepted { get; set; }
    public DateTime? AcceptedAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string Source { get; set; } = string.Empty;
    public string? Notes { get; set; }
}
