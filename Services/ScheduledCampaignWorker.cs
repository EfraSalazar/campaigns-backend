using EventCampaignSystem.Data;
using EventCampaignSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventCampaignSystem.Services;

/// <summary>
/// Revisa cada minuto si hay campañas programadas (Status="Scheduled") cuya fecha ya venció
/// y dispara su envío. Las fechas se comparan en hora de pared CDMX (convención MexicoNow,
/// igual que registration-backend), porque el admin captura la hora local de México y el
/// VPS corre en UTC.
/// </summary>
public class ScheduledCampaignWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(60);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly CampaignSendService _sendService;
    private readonly ILogger<ScheduledCampaignWorker> _logger;

    public ScheduledCampaignWorker(
        IServiceScopeFactory scopeFactory,
        CampaignSendService sendService,
        ILogger<ScheduledCampaignWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _sendService = sendService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker de campañas programadas iniciado (revisa cada {Seconds}s)", PollInterval.TotalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDueCampaignsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revisando campañas programadas");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task ProcessDueCampaignsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();

        var now = CampaignSendService.MexicoNow();
        var due = await context.Campaigns
            .Where(c => c.Status == "Scheduled" && c.ScheduledAt != null && c.ScheduledAt <= now)
            .ToListAsync();

        if (due.Count == 0) return;

        var emailService = scope.ServiceProvider.GetRequiredService<CampaignEmailService>();
        var whatsAppService = scope.ServiceProvider.GetRequiredService<CampaignWhatsAppService>();
        var sending = scope.ServiceProvider.GetRequiredService<IOptions<SendingSettings>>().Value;

        foreach (var campaign in due)
        {
            var channel = campaign.Channel;
            var isEmail = channel.Equals("Email", StringComparison.OrdinalIgnoreCase);
            var isWhatsApp = channel.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase);

            string? blocker = null;
            if (!isEmail && !isWhatsApp) blocker = $"Canal no soportado: {channel}";
            else if (isEmail && !emailService.IsConfigured) blocker = "Servicio de correo no configurado";
            else if (isWhatsApp && !whatsAppService.IsConfigured) blocker = "Servicio de WhatsApp no configurado";
            else if (sending.TestMode && string.IsNullOrWhiteSpace(isEmail ? sending.TestEmail : sending.TestPhone))
                blocker = "Modo Prueba activo sin destino de prueba configurado";
            else if (!await context.CampaignRecipients.AnyAsync(r => r.CampaignId == campaign.Id))
                blocker = "La campaña no tiene destinatarios";

            if (blocker != null)
            {
                _logger.LogError("Campaña programada {CampaignId} ({Name}) no se pudo enviar: {Reason}",
                    campaign.Id, campaign.Name, blocker);
                campaign.Status = "Failed";
                await context.SaveChangesAsync();
                continue;
            }

            _logger.LogInformation("Campaña programada {CampaignId} ({Name}) vencida ({ScheduledAt:yyyy-MM-dd HH:mm} CDMX): disparando envío por {Channel}",
                campaign.Id, campaign.Name, campaign.ScheduledAt, channel);

            var started = await _sendService.TryStartSendAsync(campaign.Id, channel, isEmail, isWhatsApp);
            if (!started)
            {
                _logger.LogWarning("Campaña programada {CampaignId} ya se estaba enviando; se omite", campaign.Id);
            }
        }
    }
}
