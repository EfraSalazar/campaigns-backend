namespace EventCampaignSystem.DTOs;

public class CampaignCreateRequest
{
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string MessageTemplate { get; set; } = string.Empty;
    public DateTime? ScheduledAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

public class CampaignResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public string Channel { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string MessageTemplate { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime? SentAt { get; set; }
    public int RecipientCount { get; set; }
}

public class CampaignRecipientResponse
{
    public int Id { get; set; }
    public int ContactId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string RecipientAddress { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public int? SourceEventId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? SentAt { get; set; }
    // Estado por canal según CommunicationLogs (multicanal).
    public string? EmailStatus { get; set; }
    public string? WhatsAppStatus { get; set; }
    // Motivo del último fallo, para mostrarlo en el panel.
    public string? ErrorMessage { get; set; }
}

public class AddRecipientsFromFilterRequest : ContactFilterRequest
{
}

public class ScheduleCampaignRequest
{
    // Hora de pared CDMX (convención MexicoNow).
    public DateTime ScheduledAt { get; set; }
    public string? Channel { get; set; }
}

public class AddRecipientsByIdsRequest
{
    public List<int> ContactIds { get; set; } = new();
    public int? SourceEventId { get; set; }
}

public class CampaignAttachmentResponse
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public int Size { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class PreviewRecipientsResponse
{
    public int Total { get; set; }
    public List<ContactResponse> Sample { get; set; } = new();
}
