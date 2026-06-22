using EventCampaignSystem.Models;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace EventCampaignSystem.Services;

public class CampaignEmailService
{
    private readonly EmailSettings _settings;
    private readonly ILogger<CampaignEmailService> _logger;

    public CampaignEmailService(IOptions<EmailSettings> settings, ILogger<CampaignEmailService> logger)
    {
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.SmtpServer) &&
        _settings.Port > 0 &&
        !string.IsNullOrWhiteSpace(_settings.Sender) &&
        !string.IsNullOrWhiteSpace(_settings.Username) &&
        !string.IsNullOrWhiteSpace(_settings.Password);

    public record Attachment(string FileName, string ContentType, byte[] Content);

    public async Task SendAsync(string toAddress, string? toName, string subject, string htmlBody, IEnumerable<Attachment>? attachments = null)
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Configuración de correo (EmailSettings) incompleta.");
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.DisplayName, _settings.Sender));
        message.To.Add(new MailboxAddress(toName ?? toAddress, toAddress));
        if (!string.IsNullOrWhiteSpace(_settings.AdminEmail) &&
            !_settings.AdminEmail.Equals(toAddress, StringComparison.OrdinalIgnoreCase))
        {
            message.Bcc.Add(new MailboxAddress(string.Empty, _settings.AdminEmail));
        }
        message.Subject = string.IsNullOrWhiteSpace(subject) ? "INTIMOS" : subject;

        var bodyBuilder = new BodyBuilder { HtmlBody = htmlBody };
        if (attachments != null)
        {
            foreach (var att in attachments)
            {
                bodyBuilder.Attachments.Add(att.FileName, att.Content, ContentType.Parse(att.ContentType));
            }
        }
        message.Body = bodyBuilder.ToMessageBody();

        using var client = new SmtpClient();
        var security = _settings.Port == 587 ? SecureSocketOptions.StartTls : SecureSocketOptions.Auto;

        await client.ConnectAsync(_settings.SmtpServer, _settings.Port, security);
        await client.AuthenticateAsync(_settings.Username, _settings.Password);
        await client.SendAsync(message);
        await client.DisconnectAsync(true);

        _logger.LogInformation("Correo de campaña enviado a {Address}", toAddress);
    }
}
