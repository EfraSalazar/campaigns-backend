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
    private readonly CampaignSendService _sendService;

    public CampaignsController(
        CampaignDbContext context,
        ContactQueryService contactQueryService,
        CampaignEmailService emailService,
        CampaignWhatsAppService whatsAppService,
        Microsoft.Extensions.Options.IOptions<Models.SendingSettings> sending,
        CampaignSendService sendService)
    {
        _context = context;
        _contactQueryService = contactQueryService;
        _emailService = emailService;
        _whatsAppService = whatsAppService;
        _sending = sending.Value;
        _sendService = sendService;
    }

    [HttpGet]
    public async Task<IActionResult> GetCampaigns()
    {
        // Projection en SQL (COUNT por subquery) en vez de Include(Recipients): evita cargar
        // todos los destinatarios de todas las campañas solo para contarlos.
        var campaigns = await _context.Campaigns
            .OrderByDescending(c => c.CreatedAt)
            .Select(c => new CampaignResponse
            {
                Id = c.Id,
                Name = c.Name,
                Purpose = c.Purpose,
                Channel = c.Channel,
                Status = c.Status,
                Subject = c.Subject,
                MessageTemplate = c.MessageTemplate,
                CreatedAt = c.CreatedAt,
                ScheduledAt = c.ScheduledAt,
                SentAt = c.SentAt,
                RecipientCount = c.Recipients.Count
            })
            .ToListAsync();

        return Ok(campaigns);
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

        // Omitir a quienes ya están agregados como destinatarios de esta campaña
        // (no tiene sentido volver a mostrarlos en la lista para elegir).
        var alreadyAddedIds = await _context.CampaignRecipients
            .Where(r => r.CampaignId == id)
            .Select(r => r.ContactId)
            .ToListAsync();
        if (alreadyAddedIds.Count > 0)
        {
            query = query.Where(c => !alreadyAddedIds.Contains(c.Id));
        }

        var total = await query.CountAsync();
        var sample = await ContactQueryService.ProjectToResponse(
                query.OrderBy(c => c.State)
                    .ThenBy(c => c.City)
                    .ThenBy(c => c.FirstName)
                    .Take(Math.Clamp(filter.Limit, 1, 2000)))
            .ToListAsync();

        return Ok(new PreviewRecipientsResponse
        {
            Total = total,
            Sample = sample
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

    // Tipos de archivo permitidos como adjunto: imágenes, PDF, Office y texto plano.
    private static readonly HashSet<string> AllowedAttachmentExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".gif", ".webp",
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".txt"
    };

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

        var extension = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(extension) || !AllowedAttachmentExtensions.Contains(extension))
        {
            return BadRequest(new { error = $"Tipo de archivo no permitido ({extension}). Usa imagen, PDF, documento de Office o texto." });
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

        if (_sendService.IsSending(id))
        {
            return Conflict(new { error = "Esta campaña ya se está enviando." });
        }
        // Si el Status en BD dice "Sending" pero no hay tarea activa en este proceso, el envío
        // quedó huérfano (crash o tarea muerta): se permite reanudar. El dedup por campaña solo
        // omite a los ya enviados, así que continúa con los pendientes/fallidos sin duplicar.

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

        // Saltamos contactos ya enviados con éxito en ESTA campaña (por canal), para no
        // duplicar si se reintenta el envío. La deduplicación es por campaña: cada campaña
        // nueva puede llegar a todos otra vez, aunque ya hayan recibido campañas anteriores.
        var alreadySentContactIds = await _context.CommunicationLogs
            .Where(l => l.CampaignId == campaign.Id && l.Status == "Sent" && l.Channel == effectiveChannel
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

        // El servicio reclama el envío de forma atómica (dos clics simultáneos → uno recibe 409)
        // y lo corre en segundo plano; ver CampaignSendService.
        var started = await _sendService.TryStartSendAsync(campaign.Id, effectiveChannel, isEmail, isWhatsApp);
        if (!started)
        {
            return Conflict(new { error = "Esta campaña ya se está enviando." });
        }

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


    [HttpPost("{id:int}/schedule")]
    public async Task<IActionResult> ScheduleCampaign(int id, ScheduleCampaignRequest request)
    {
        var campaign = await _context.Campaigns.FindAsync(id);
        if (campaign == null) return NotFound(new { error = $"No se encontró la campaña {id}" });

        if (_sendService.IsSending(id) || campaign.Status == "Sending")
        {
            return Conflict(new { error = "Esta campaña se está enviando; no se puede programar." });
        }

        var channel = string.IsNullOrWhiteSpace(request.Channel) ? campaign.Channel : request.Channel.Trim();
        var isEmail = channel.Equals("Email", StringComparison.OrdinalIgnoreCase);
        var isWhatsApp = channel.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase);
        if (!isEmail && !isWhatsApp)
        {
            return BadRequest(new { error = $"Canal no soportado: {channel}." });
        }

        var now = CampaignSendService.MexicoNow();
        if (request.ScheduledAt <= now)
        {
            return BadRequest(new { error = $"La fecha programada debe ser futura (hora CDMX actual: {now:yyyy-MM-dd HH:mm})." });
        }

        var hasRecipients = await _context.CampaignRecipients.AnyAsync(r => r.CampaignId == id);
        if (!hasRecipients)
        {
            return BadRequest(new { error = "Agrega destinatarios antes de programar el envío." });
        }

        campaign.Channel = channel;
        campaign.ScheduledAt = request.ScheduledAt;
        campaign.Status = "Scheduled";
        await _context.SaveChangesAsync();

        campaign = (await _context.Campaigns.Include(c => c.Recipients).FirstOrDefaultAsync(c => c.Id == id))!;
        return Ok(ToResponse(campaign));
    }

    [HttpDelete("{id:int}/schedule")]
    public async Task<IActionResult> CancelSchedule(int id)
    {
        var campaign = await _context.Campaigns.FindAsync(id);
        if (campaign == null) return NotFound(new { error = $"No se encontró la campaña {id}" });

        if (campaign.Status != "Scheduled")
        {
            return BadRequest(new { error = "Esta campaña no está programada." });
        }

        campaign.Status = "Draft";
        campaign.ScheduledAt = null;
        await _context.SaveChangesAsync();

        campaign = (await _context.Campaigns.Include(c => c.Recipients).FirstOrDefaultAsync(c => c.Id == id))!;
        return Ok(ToResponse(campaign));
    }

    [HttpDelete("{id:int}/recipients/{recipientId:int}")]
    public async Task<IActionResult> RemoveRecipient(int id, int recipientId)
    {
        var recipient = await _context.CampaignRecipients
            .FirstOrDefaultAsync(r => r.Id == recipientId && r.CampaignId == id);
        if (recipient == null) return NotFound(new { error = "No se encontró el destinatario." });

        // Los CommunicationLogs se conservan: son el historial de lo que realmente se envió.
        _context.CampaignRecipients.Remove(recipient);
        await _context.SaveChangesAsync();
        return Ok(new { deleted = recipientId });
    }

    [HttpGet("{id:int}/logs")]
    public async Task<IActionResult> GetLogs(int id)
    {
        var campaignExists = await _context.Campaigns.AnyAsync(c => c.Id == id);
        if (!campaignExists) return NotFound(new { error = $"No se encontró la campaña {id}" });

        var logs = await _context.CommunicationLogs
            .Where(l => l.CampaignId == id)
            .OrderByDescending(l => l.CreatedAt)
            .Take(500)
            .Select(l => new
            {
                l.Id,
                l.Channel,
                l.Recipient,
                l.Status,
                l.ErrorMessage,
                l.CreatedAt,
                ContactName = l.Contact != null
                    ? (l.Contact.FirstName + " " + l.Contact.LastName).Trim()
                    : null
            })
            .ToListAsync();

        return Ok(logs);
    }

    [HttpGet("{id:int}/render")]
    public async Task<IActionResult> RenderPreview(int id, [FromQuery] int? contactId = null)
    {
        var campaign = await _context.Campaigns.FindAsync(id);
        if (campaign == null) return NotFound(new { error = $"No se encontró la campaña {id}" });

        Models.Contact? contact = null;
        int? sourceEventId = null;

        if (contactId.HasValue)
        {
            contact = await _context.Contacts.FindAsync(contactId.Value);
            sourceEventId = await _context.CampaignRecipients
                .Where(r => r.CampaignId == id && r.ContactId == contactId.Value)
                .Select(r => r.SourceEventId)
                .FirstOrDefaultAsync();
        }
        else
        {
            // Sin contactId se usa el primer destinatario de la campaña como muestra.
            var first = await _context.CampaignRecipients
                .Include(r => r.Contact)
                .Where(r => r.CampaignId == id)
                .OrderBy(r => r.Id)
                .FirstOrDefaultAsync();
            contact = first?.Contact;
            sourceEventId = first?.SourceEventId;
        }

        var eventName = string.Empty;
        if (sourceEventId.HasValue)
        {
            eventName = await _context.Events
                .Where(e => e.Id == sourceEventId.Value)
                .Select(e => e.Name)
                .FirstOrDefaultAsync() ?? string.Empty;
        }

        var rendered = CampaignSendService.RenderText(campaign.MessageTemplate, contact, eventName);
        return Ok(new
        {
            contactName = contact != null ? $"{contact.FirstName} {contact.LastName}".Trim() : null,
            rendered
        });
    }

    [HttpPost("{id:int}/duplicate")]
    public async Task<IActionResult> DuplicateCampaign(int id)
    {
        var campaign = await _context.Campaigns.FindAsync(id);
        if (campaign == null) return NotFound(new { error = $"No se encontró la campaña {id}" });

        var copy = new Campaign
        {
            Name = $"{campaign.Name} (copia)",
            Purpose = campaign.Purpose,
            Channel = campaign.Channel,
            Subject = campaign.Subject,
            MessageTemplate = campaign.MessageTemplate,
            CreatedBy = campaign.CreatedBy,
            Status = "Draft",
            CreatedAt = DateTime.UtcNow
        };
        _context.Campaigns.Add(copy);
        await _context.SaveChangesAsync();

        // Copiar destinatarios (en Pending: la copia parte de cero, sin historial de envíos).
        var recipientsToCopy = await _context.CampaignRecipients
            .Where(r => r.CampaignId == id)
            .ToListAsync();
        foreach (var r in recipientsToCopy)
        {
            _context.CampaignRecipients.Add(new CampaignRecipient
            {
                CampaignId = copy.Id,
                ContactId = r.ContactId,
                SourceEventId = r.SourceEventId,
                RecipientAddress = r.RecipientAddress,
                Status = "Pending",
                CreatedAt = DateTime.UtcNow
            });
        }

        var attachmentsToCopy = await _context.CampaignAttachments
            .Where(a => a.CampaignId == id)
            .ToListAsync();
        foreach (var a in attachmentsToCopy)
        {
            _context.CampaignAttachments.Add(new CampaignAttachment
            {
                CampaignId = copy.Id,
                FileName = a.FileName,
                ContentType = a.ContentType,
                Content = a.Content,
                Size = a.Size,
                CreatedAt = DateTime.UtcNow
            });
        }

        await _context.SaveChangesAsync();

        var created = (await _context.Campaigns.Include(c => c.Recipients).FirstOrDefaultAsync(c => c.Id == copy.Id))!;
        return CreatedAtAction(nameof(GetCampaign), new { id = copy.Id }, ToResponse(created));
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
                SentAt = r.SentAt,
                ErrorMessage = r.ErrorMessage
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
