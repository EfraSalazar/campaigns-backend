using System.ComponentModel.DataAnnotations;

namespace EventCampaignSystem.Models;

public class CampaignAttachment
{
    [Key]
    public int Id { get; set; }

    public int CampaignId { get; set; }

    [MaxLength(260)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(120)]
    public string ContentType { get; set; } = string.Empty;

    public byte[] Content { get; set; } = Array.Empty<byte>();

    public int Size { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Campaign Campaign { get; set; } = null!;
}
