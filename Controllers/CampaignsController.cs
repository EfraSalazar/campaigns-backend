using EventCampaignSystem.Data;
using Microsoft.AspNetCore.Authorization;
using EventCampaignSystem.DTOs;
using EventCampaignSystem.Models;
using EventCampaignSystem.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EventCampaignSystem.Controllers;

[Authorize(Roles = "Admin")]
[ApiController]
[Route("api/[controller]")]
public class CampaignsController : ControllerBase
{
    private readonly CampaignDbContext _context;
    private readonly ContactQueryService _contactQueryService;
    private readonly CampaignEmailService _emailService;
    private readonly CampaignWhatsAppService _whatsAppService;
    private readonly Models.SendingSettings _sending;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<CampaignsController> _logger;

    public CampaignsController(
        CampaignDbContext context,
        ContactQueryService contactQueryService,
        CampaignEmailService emailService,
        CampaignWhatsAppService whatsAppService,
        Microsoft.Extensions.Options.IOptions<Models.SendingSettings> sending,
        IServiceScopeFactory scopeFactory,
        ILogger<CampaignsController> logger)
    {
        _context = context;
        _contactQueryService = contactQueryService;
        _emailService = emailService;
        _whatsAppService = whatsAppService;
        _sending = sending.Value;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> GetCampaigns()
    {
        var campaigns = await _context.Campaigns
            .Include(c => c.Recipients)
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync();

        return Ok(campaigns.Select(ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> CreateCampaign(CampaignCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Purpose) ||
            string.IsNullOrWhiteSpace(request.Channel) ||
            string.IsNullOrWhiteSpace(request.MessageTemplate))
        {
            return BadRequest(new { error = "Name, Purpose, Channel y MessageTemplate son obligatorios" });
        }

        var campaign = new Campaign
        {
            Name = request.Name.Trim(),
            Purpose = request.Purpose.Trim(),
            Channel = request.Channel.Trim(),
            Subject = request.Subject?.Trim(),
            MessageTemplate = request.MessageTemplate.Trim(),
            ScheduledAt = request.ScheduledAt,
            CreatedBy = request.CreatedBy?.Trim() ?? string.Empty,
            Status = "Draft",
            CreatedAt = DateTime.UtcNow
        };

        _context.Campaigns.Add(campaign);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetCampaign), new { id = campaign.Id }, ToResponse(campaign));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetCampaign(int id)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.Recipients)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
        {
            return NotFound(new { error = $"No se encontro la campana {id}" });
        }

        return Ok(ToResponse(campaign));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCampaign(int id, CampaignCreateRequest request)
    {
        var campaign = await _context.Campaigns.FindAsync(id);
        if (campaign == null) return NotFound(new { error = $"No se encontró la campaña {id}" });

        if (!string.IsNullOrWhiteSpace(request.Name)) campaign.Name = request.Name.Trim();
        if (!string.IsNullOrWhiteSpace(request.Purpose)) campaign.Purpose = request.Purpose.Trim();
        if (!string.IsNullOrWhiteSpace(request.Channel)) campaign.Channel = request.Channel.Trim();
        if (!string.IsNullOrWhiteSpace(request.MessageTemplate)) campaign.MessageTemplate = request.MessageTemplate.Trim();
        campaign.Subject = request.Subject?.Trim();
        campaign.ScheduledAt = request.ScheduledAt;

        await _context.SaveChangesAsync();
        campaign = (await _context.Campaigns.Include(c => c.Recipients).FirstOrDefaultAsync(c => c.Id == id))!;
        return Ok(ToResponse(campaign));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCampaign(int id)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.Recipients)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null) return NotFound(new { error = $"No se encontró la campaña {id}" });

        var attachments = await _context.CampaignAttachments.Where(a => a.CampaignId == id).ToListAsync();
        _context.CampaignAttachments.RemoveRange(attachments);

        var logs = await _context.CommunicationLogs.Where(l => l.CampaignId == id).ToListAsync();
        _context.CommunicationLogs.RemoveRange(logs);

        _context.CampaignRecipients.RemoveRange(campaign.Recipients);
        _context.Campaigns.Remove(campaign);
        await _context.SaveChangesAsync();

        return Ok(new { deleted = id });
    }

    [HttpPost("{id:int}/recipients/preview")]
    public async Task<IActionResult> PreviewRecipients(int id, AddRecipientsFromFilterRequest filter)
    {
        var campaign = await _context.Campaigns.FindAsync(id);
        if (campaign == null)
        {
            return NotFound(new { error = $"No se encontro la campana {id}" });
        }

        var query = _contactQueryService.BuildQuery(filter);
        var total = await query.CountAsync();
        var sample = await query
            .OrderBy(c => c.State)
            .ThenBy(c => c.City)
            .ThenBy(c => c.FirstName)
            .Take(Math.Clamp(filter.Limit, 1, 2000))
            .ToListAsync();

        return Ok(new PreviewRecipientsResponse
        {
            Total = total,
            Sample = sample.Select(ContactQueryService.ToResponse).ToList()
        });
    }

    [HttpPost("{id:int}/recipients/from-filter")]
    public async Task<IActionResult> AddRecipientsFromFilter(int id, AddRecipientsFromFilterRequest filter)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.Recipients)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
        {
            return NotFound(new { error = $"No se encontro la campana {id}" });
        }

        var contacts = await _contactQueryService.BuildQuery(filter)
            .Take(Math.Clamp(filter.Limit, 1, 5000))
            .ToListAsync();

        var existingContactIds = campaign.Recipients.Select(r => r.ContactId).ToHashSet();
        var added = 0;

        foreach (var contact in contacts)
        {
            if (existingContactIds.Contains(contact.Id))
            {
                continue;
            }

            var recipientAddress = campaign.Channel.Equals("Email", StringComparison.OrdinalIgnoreCase)
                ? contact.Email
                : contact.PhoneNumber;

            if (string.IsNullOrWhiteSpace(recipientAddress))
            {
                continue;
            }

            _context.CampaignRecipients.Add(new CampaignRecipient
            {
                CampaignId = campaign.Id,
                ContactId = contact.Id,
                SourceEventId = filter.EventId,
                RecipientAddress = recipientAddress,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            });

            added++;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            campaignId = campaign.Id,
            scanned = contacts.Count,
            added
        });
    }

    [HttpPost("{id:int}/recipients/from-contacts")]
    public async Task<IActionResult> AddRecipientsByIds(int id, AddRecipientsByIdsRequest request)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.Recipients)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
        {
            return NotFound(new { error = $"No se encontro la campana {id}" });
        }

        var contactIds = request.ContactIds.Distinct().ToList();
        if (contactIds.Count == 0)
        {
            return BadRequest(new { error = "ContactIds no puede estar vacio" });
        }

        var contacts = await _context.Contacts
            .Where(c => contactIds.Contains(c.Id))
            .ToListAsync();

        var existingContactIds = campaign.Recipients.Select(r => r.ContactId).ToHashSet();
        var added = 0;
        var skipped = 0;

        foreach (var contact in contacts)
        {
            if (existingContactIds.Contains(contact.Id))
            {
                skipped++;
                continue;
            }

            var recipientAddress = campaign.Channel.Equals("Email", StringComparison.OrdinalIgnoreCase)
                ? contact.Email
                : contact.PhoneNumber;

            if (string.IsNullOrWhiteSpace(recipientAddress))
            {
                skipped++;
                continue;
            }

            _context.CampaignRecipients.Add(new CampaignRecipient
            {
                CampaignId = campaign.Id,
                ContactId = contact.Id,
                SourceEventId = request.SourceEventId,
                RecipientAddress = recipientAddress,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            });

            added++;
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            campaignId = campaign.Id,
            scanned = contacts.Count,
            added,
            skipped
        });
    }

    [HttpGet("{id:int}/attachments")]
    public async Task<IActionResult> GetAttachments(int id)
    {
        var exists = await _context.Campaigns.AnyAsync(c => c.Id == id);
        if (!exists)
        {
            return NotFound(new { error = $"No se encontro la campana {id}" });
        }

        var items = await _context.CampaignAttachments
            .Where(a => a.CampaignId == id)
            .OrderBy(a => a.CreatedAt)
            .Select(a => new CampaignAttachmentResponse
            {
                Id = a.Id,
                FileName = a.FileName,
                ContentType = a.ContentType,
                Size = a.Size,
                CreatedAt = a.CreatedAt
            })
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost("{id:int}/attachments")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> UploadAttachment(int id, IFormFile file)
    {
        var campaign = await _context.Campaigns.FindAsync(id);
        if (campaign == null)
        {
            return NotFound(new { error = $"No se encontro la campana {id}" });
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { error = "Archivo vacío." });
        }

        if (file.Length > 16_000_000)
        {
            return BadRequest(new { error = "El archivo supera el límite de 16 MB." });
        }

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms);
        var content = ms.ToArray();

        var attachment = new CampaignAttachment
        {
            CampaignId = campaign.Id,
            FileName = Path.GetFileName(file.FileName),
            ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
            Content = content,
            Size = content.Length,
            CreatedAt = DateTime.UtcNow
        };

        _context.CampaignAttachments.Add(attachment);
        await _context.SaveChangesAsync();

        return Ok(new CampaignAttachmentResponse
        {
            Id = attachment.Id,
            FileName = attachment.FileName,
            ContentType = attachment.ContentType,
            Size = attachment.Size,
            CreatedAt = attachment.CreatedAt
        });
    }

    [HttpDelete("{id:int}/attachments/{attachmentId:int}")]
    public async Task<IActionResult> DeleteAttachment(int id, int attachmentId)
    {
        var attachment = await _context.CampaignAttachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.CampaignId == id);

        if (attachment == null)
        {
            return NotFound(new { error = "No se encontró el adjunto." });
        }

        _context.CampaignAttachments.Remove(attachment);
        await _context.SaveChangesAsync();
        return Ok(new { deleted = attachmentId });
    }

    [HttpPost("{id:int}/send")]
    public async Task<IActionResult> SendCampaign(int id, [FromQuery] string? channel = null)
    {
        var campaign = await _context.Campaigns
            .Include(c => c.Recipients)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (campaign == null)
        {
            return NotFound(new { error = $"No se encontro la campana {id}" });
        }

        if (campaign.Status == "Sending")
        {
            return Conflict(new { error = "Esta campaña ya se está enviando." });
        }

        // El canal se puede elegir al enviar; si no se indica, se usa el de la campaña.
        var effectiveChannel = string.IsNullOrWhiteSpace(channel) ? campaign.Channel : channel.Trim();
        var isEmail = effectiveChannel.Equals("Email", StringComparison.OrdinalIgnoreCase);
        var isWhatsApp = effectiveChannel.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase);

        if (!isEmail && !isWhatsApp)
        {
            return BadRequest(new { error = $"Canal no soportado para envío: {effectiveChannel}." });
        }

        if (isEmail && !_emailService.IsConfigured)
        {
            return StatusCode(503, new { error = "El servicio de correo (EmailSettings) no está configurado." });
        }

        if (isWhatsApp && !_whatsAppService.IsConfigured)
        {
            return StatusCode(503, new { error = "El servicio de WhatsApp (WhatsAppSettings) no está configurado." });
        }

        // Multicanal: procesar a quienes aún NO recibieron con éxito por ESTE canal
        // (según CommunicationLogs). Así el mismo destinatario puede recibir por Email y por WhatsApp.
        var campaignContactIds = campaign.Recipients.Select(r => r.ContactId).ToHashSet();

        // Saltamos contactos ya enviados en ESTA campaña (por canal) + en CUALQUIER otra campaña.
        var alreadySentContactIds = await _context.CommunicationLogs
            .Where(l => l.Status == "Sent" && l.Channel == effectiveChannel
                     && l.ContactId != null && campaignContactIds.Contains(l.ContactId!.Value))
            .Select(l => l.ContactId!.Value)
            .Distinct()
            .ToListAsync();
        var sentSet = alreadySentContactIds.ToHashSet();

        var pendingCount = campaign.Recipients.Count(r => !sentSet.Contains(r.ContactId));

        if (pendingCount == 0)
        {
            return Ok(new { campaignId = campaign.Id, channel = effectiveChannel, status = campaign.Status, sent = 0, failed = 0, message = $"Todos ya recibieron por {effectiveChannel}." });
        }

        // Modo Prueba: validar destino de prueba antes de arrancar el envío.
        var testTarget = isEmail ? _sending.TestEmail : _sending.TestPhone;
        if (_sending.TestMode && string.IsNullOrWhiteSpace(testTarget))
        {
            return StatusCode(503, new { error = $"Modo Prueba activo pero falta {(isEmail ? "Sending:TestEmail" : "Sending:TestPhone")}." });
        }

        campaign.Status = "Sending";
        await _context.SaveChangesAsync();

        // El envío corre en segundo plano (no atado al request HTTP) porque entre mensajes de WhatsApp
        // se aplica una demora aleatoria de 30-90s para que la campaña no se vea como mensajería masiva;
        // eso fácilmente supera el timeout del cliente si se hiciera de forma síncrona.
        _ = Task.Run(() => ProcessCampaignSendAsync(campaign.Id, effectiveChannel, isEmail, isWhatsApp));

        return Accepted(new
        {
            campaignId = campaign.Id,
            channel = effectiveChannel,
            status = "Sending",
            pending = pendingCount,
            testMode = _sending.TestMode,
            redirectedTo = _sending.TestMode ? testTarget : null
        });
    }

    private async Task ProcessCampaignSendAsync(int campaignId, string effectiveChannel, bool isEmail, bool isWhatsApp)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<CampaignEmailService>();
        var whatsAppService = scope.ServiceProvider.GetRequiredService<CampaignWhatsAppService>();
        var sending = scope.ServiceProvider.GetRequiredService<IOptions<Models.SendingSettings>>().Value;

        var sent = 0;
        var failed = 0;

        try
        {
            var campaign = await context.Campaigns
                .Include(c => c.Recipients)
                .FirstOrDefaultAsync(c => c.Id == campaignId);
            if (campaign == null) return;

            var campaignContactIds = campaign.Recipients.Select(r => r.ContactId).ToHashSet();
            var alreadySentContactIds = await context.CommunicationLogs
                .Where(l => l.Status == "Sent" && l.Channel == effectiveChannel
                         && l.ContactId != null && campaignContactIds.Contains(l.ContactId!.Value))
                .Select(l => l.ContactId!.Value)
                .Distinct()
                .ToListAsync();
            var sentSet = alreadySentContactIds.ToHashSet();

            var pending = campaign.Recipients
                .Where(r => !sentSet.Contains(r.ContactId))
                .ToList();

            var testTarget = isEmail ? sending.TestEmail : sending.TestPhone;
            if (sending.TestMode)
            {
                var cap = Math.Max(1, sending.MaxRecipientsInTestMode);
                if (pending.Count > cap)
                {
                    pending = pending.Take(cap).ToList();
                }
            }

            var contactIds = pending.Select(r => r.ContactId).ToList();
            var contacts = await context.Contacts
                .Where(c => contactIds.Contains(c.Id))
                .ToDictionaryAsync(c => c.Id);

            var eventIds = pending.Where(r => r.SourceEventId.HasValue).Select(r => r.SourceEventId!.Value).Distinct().ToList();
            var events = await context.Events
                .Where(e => eventIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.Name);

            var attachmentRecords = await context.CampaignAttachments
                .Where(a => a.CampaignId == campaign.Id)
                .ToListAsync();
            var emailAttachments = attachmentRecords
                .Select(a => new CampaignEmailService.Attachment(a.FileName, a.ContentType, a.Content))
                .ToList();
            var firstFile = attachmentRecords.Count > 0
                ? new CampaignWhatsAppService.FileAttachment(
                    attachmentRecords[0].FileName, attachmentRecords[0].ContentType, attachmentRecords[0].Content)
                : null;

            var isFirstSend = true;

            foreach (var recipient in pending)
            {
                contacts.TryGetValue(recipient.ContactId, out var contact);
                var eventName = recipient.SourceEventId.HasValue && events.TryGetValue(recipient.SourceEventId.Value, out var en)
                    ? en
                    : string.Empty;

                var text = RenderText(campaign.MessageTemplate, contact, eventName);
                var displayName = contact != null
                    ? $"{contact.FirstName} {contact.LastName}".Trim()
                    : recipient.RecipientAddress;

                // El destino real se resuelve del contacto según el canal elegido.
                var realAddress = isEmail
                    ? (contact?.Email ?? string.Empty)
                    : (contact?.PhoneNumber ?? string.Empty);

                // Respaldo: si el contacto no trae el dato pero el address guardado sirve para este canal.
                if (string.IsNullOrWhiteSpace(realAddress) &&
                    recipient.RecipientAddress.Contains('@') == isEmail)
                {
                    realAddress = recipient.RecipientAddress;
                }

                if (string.IsNullOrWhiteSpace(realAddress))
                {
                    recipient.Status = "Failed";
                    recipient.ErrorMessage = isEmail ? "Sin correo" : "Sin teléfono";
                    failed++;
                    context.CommunicationLogs.Add(new CommunicationLog
                    {
                        CampaignId = campaign.Id,
                        ContactId = recipient.ContactId,
                        Channel = effectiveChannel,
                        Recipient = recipient.RecipientAddress,
                        Status = "Failed",
                        ErrorMessage = recipient.ErrorMessage,
                        CreatedAt = DateTime.UtcNow
                    });
                    await context.SaveChangesAsync();
                    continue;
                }

                // En Modo Prueba se redirige el destino real al de prueba y se marca el contenido.
                var destination = sending.TestMode ? testTarget : realAddress;
                if (sending.TestMode)
                {
                    text = $"[PRUEBA → {realAddress}]\n\n{text}";
                }

                // Demora aleatoria de 30-90s entre mensajes de WhatsApp para que el envío no se
                // vea como mensajería masiva (no se aplica antes del primer mensaje).
                if (isWhatsApp && !isFirstSend)
                {
                    await whatsAppService.DelayBeforeNextSendAsync();
                }
                isFirstSend = false;

                string status;
                string? error = null;
                string? providerResponse = null;

                try
                {
                    if (isEmail)
                    {
                        var html = TextToHtml(text);
                        var subject = sending.TestMode ? $"[PRUEBA] {campaign.Subject ?? campaign.Name}" : (campaign.Subject ?? campaign.Name);
                        await emailService.SendAsync(destination, displayName, subject, html, emailAttachments);
                        status = "Sent";
                    }
                    else
                    {
                        var result = await whatsAppService.SendTextAsync(destination, text, firstFile);
                        providerResponse = result.ProviderResponse;
                        if (result.Success)
                        {
                            status = "Sent";
                        }
                        else
                        {
                            status = "Failed";
                            error = result.Error;
                        }
                    }
                }
                catch (Exception ex)
                {
                    status = "Failed";
                    error = ex.Message;
                }

                if (status == "Sent")
                {
                    recipient.Status = "Sent";
                    recipient.SentAt = DateTime.UtcNow;
                    recipient.ErrorMessage = null;
                    sent++;
                }
                else
                {
                    recipient.Status = "Failed";
                    recipient.ErrorMessage = error;
                    failed++;
                }

                context.CommunicationLogs.Add(new CommunicationLog
                {
                    CampaignId = campaign.Id,
                    ContactId = recipient.ContactId,
                    Channel = effectiveChannel,
                    Recipient = realAddress,
                    Status = status,
                    ProviderResponse = providerResponse,
                    ErrorMessage = error,
                    CreatedAt = DateTime.UtcNow
                });

                // Se guarda después de cada destinatario para que el polling del frontend vea el progreso en vivo.
                await context.SaveChangesAsync();
            }

            campaign.Status = sent > 0 ? "Sent" : "Failed";
            if (sent > 0) campaign.SentAt = DateTime.UtcNow;
            await context.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error procesando el envío de la campaña {CampaignId}", campaignId);
            try
            {
                var campaign = await context.Campaigns.FindAsync(campaignId);
                if (campaign != null)
                {
                    campaign.Status = sent > 0 ? "Sent" : "Failed";
                    await context.SaveChangesAsync();
                }
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "No se pudo marcar como fallida la campaña {CampaignId} tras un error", campaignId);
            }
        }
    }

    private static string RenderText(string template, Models.Contact? contact, string eventName)
    {
        if (string.IsNullOrEmpty(template))
        {
            return string.Empty;
        }

        var nombre = contact != null ? contact.FirstName : string.Empty;
        return template
            .Replace("{nombre}", nombre)
            .Replace("{ciudad}", contact?.City ?? string.Empty)
            .Replace("{iglesia}", contact?.Church ?? string.Empty)
            .Replace("{evento}", eventName);
    }

    private static string TextToHtml(string text)
    {
        // Texto plano -> HTML simple, respetando saltos de línea.
        return $"<div style='font-family:Arial,sans-serif;font-size:15px;color:#333;white-space:pre-wrap;'>{System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>")}</div>";
    }

    [HttpDelete("{id:int}/recipients")]
    public async Task<IActionResult> ClearRecipients(int id)
    {
        var campaignExists = await _context.Campaigns.AnyAsync(c => c.Id == id);
        if (!campaignExists) return NotFound(new { error = $"No se encontró la campaña {id}" });

        var logs = await _context.CommunicationLogs.Where(l => l.CampaignId == id).ToListAsync();
        _context.CommunicationLogs.RemoveRange(logs);

        var recipients = await _context.CampaignRecipients.Where(r => r.CampaignId == id).ToListAsync();
        _context.CampaignRecipients.RemoveRange(recipients);

        await _context.Campaigns
            .Where(c => c.Id == id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Status, "Draft")
                .SetProperty(c => c.SentAt, (DateTime?)null));

        await _context.SaveChangesAsync();
        return Ok(new { cleared = recipients.Count });
    }

    [HttpGet("{id:int}/recipients")]
    public async Task<IActionResult> GetRecipients(int id)
    {
        var campaignExists = await _context.Campaigns.AnyAsync(c => c.Id == id);
        if (!campaignExists)
        {
            return NotFound(new { error = $"No se encontro la campana {id}" });
        }

        var recipients = await _context.CampaignRecipients
            .Include(r => r.Contact)
            .Where(r => r.CampaignId == id)
            .OrderBy(r => r.Contact.FirstName)
            .ThenBy(r => r.Contact.LastName)
            .Select(r => new CampaignRecipientResponse
            {
                Id = r.Id,
                ContactId = r.ContactId,
                FullName = $"{r.Contact.FirstName} {r.Contact.LastName} {r.Contact.SecondLastName}".Trim(),
                RecipientAddress = r.RecipientAddress,
                Status = r.Status,
                SourceEventId = r.SourceEventId,
                CreatedAt = r.CreatedAt,
                SentAt = r.SentAt
            })
            .ToListAsync();

        // Estado por canal (multicanal): último log por contacto+canal.
        var contactIds = recipients.Select(r => r.ContactId).ToList();
        var logs = await _context.CommunicationLogs
            .Where(l => l.CampaignId == id && l.ContactId != null)
            .OrderBy(l => l.CreatedAt)
            .Select(l => new { l.ContactId, l.Channel, l.Status })
            .ToListAsync();

        foreach (var r in recipients)
        {
            r.EmailStatus = logs
                .Where(l => l.ContactId == r.ContactId && l.Channel == "Email")
                .Select(l => l.Status)
                .LastOrDefault();
            r.WhatsAppStatus = logs
                .Where(l => l.ContactId == r.ContactId && l.Channel == "WhatsApp")
                .Select(l => l.Status)
                .LastOrDefault();
        }

        return Ok(recipients);
    }

    private static CampaignResponse ToResponse(Campaign campaign)
    {
        return new CampaignResponse
        {
            Id = campaign.Id,
            Name = campaign.Name,
            Purpose = campaign.Purpose,
            Channel = campaign.Channel,
            Status = campaign.Status,
            Subject = campaign.Subject,
            MessageTemplate = campaign.MessageTemplate,
            CreatedAt = campaign.CreatedAt,
            ScheduledAt = campaign.ScheduledAt,
            SentAt = campaign.SentAt,
            RecipientCount = campaign.Recipients.Count
        };
    }
}
