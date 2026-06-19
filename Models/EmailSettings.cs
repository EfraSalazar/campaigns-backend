namespace EventCampaignSystem.Models;

public class EmailSettings
{
    public string SmtpServer { get; set; } = string.Empty;
    public int Port { get; set; } = 587;
    public string Sender { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string DisplayName { get; set; } = "ÍNTIMOS";
    public string AdminEmail { get; set; } = string.Empty;
}
