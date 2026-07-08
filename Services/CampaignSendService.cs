using System.Collections.Concurrent;
using EventCampaignSystem.Data;
using EventCampaignSystem.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace EventCampaignSystem.Services;

/// <summary>
/// Ejecuta el envío de campañas en segundo plano. Es singleton: mantiene el registro de
/// envíos activos del proceso (fuente de verdad frente al Status "Sending" de la BD, que
/// puede quedar huérfano si el proceso muere) y lo usan tanto el controller (envío manual)
/// como el worker de envíos programados.
/// </summary>
public class CampaignSendService
{
    // TryAdd es atómico: si llegan dos arranques simultáneos de la misma campaña solo uno gana.
    private static readonly ConcurrentDictionary<int, byte> ActiveSends = new();

    private readonly IServiceScopeFactory _scopeFactory;

    public CampaignSendService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public bool IsSending(int campaignId) => ActiveSends.ContainsKey(campaignId);

    /// <summary>
    /// Hora de pared CDMX: misma convención que registration-backend (MexicoNow). El VPS corre
    /// en UTC; las fechas que el admin captura/ve son hora de México.
    /// </summary>
    public static DateTime MexicoNow()
    {
        var tz = TimeZoneInfo.FindSystemTimeZoneById("America/Mexico_City");
        return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
    }

    /// <summary>
    /// Reclama el envío y lo lanza en background. Devuelve false si esta campaña ya se está
    /// enviando en este proceso. El envío corre desatado del request HTTP porque entre
    /// mensajes de WhatsApp se aplica una demora aleatoria de 30-90s (una campaña grande
    /// tarda horas).
    /// </summary>
    public async Task<bool> TryStartSendAsync(int campaignId, string effectiveChannel, bool isEmail, bool isWhatsApp)
    {
        if (!ActiveSends.TryAdd(campaignId, 0))
        {
            return false;
        }

        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
                await context.Campaigns
                    .Where(c => c.Id == campaignId)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "Sending"));
            }

            // La tarea libera ActiveSends al terminar (finally en ProcessCampaignSendAsync).
            _ = Task.Run(() => ProcessCampaignSendAsync(campaignId, effectiveChannel, isEmail, isWhatsApp));
            return true;
        }
        catch
        {
            ActiveSends.TryRemove(campaignId, out _);
            throw;
        }
    }

    private async Task ProcessCampaignSendAsync(int campaignId, string effectiveChannel, bool isEmail, bool isWhatsApp)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        var emailService = scope.ServiceProvider.GetRequiredService<CampaignEmailService>();
        var whatsAppService = scope.ServiceProvider.GetRequiredService<CampaignWhatsAppService>();
        var sending = scope.ServiceProvider.GetRequiredService<IOptions<SendingSettings>>().Value;
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<CampaignSendService>>();

        var sent = 0;
        var failed = 0;

        try
        {
            var campaign = await context.Campaigns
                .Include(c => c.Recipients)
                .FirstOrDefaultAsync(c => c.Id == campaignId);
            if (campaign == null) return;

            var campaignContactIds = campaign.Recipients.Select(r => r.ContactId).ToHashSet();
            // Dedup por campaña: solo se omiten los ya enviados con éxito en ESTA misma
            // campaña (por canal), para permitir reintentos sin duplicar.
            var alreadySentContactIds = await context.CommunicationLogs
                .Where(l => l.CampaignId == campaign.Id && l.Status == "Sent" && l.Channel == effectiveChannel
                         && l.ContactId != null && campaignContactIds.Contains(l.ContactId!.Value))
                .Select(l => l.ContactId!.Value)
                .Distinct()
                .ToListAsync();
            var sentSet = alreadySentContactIds.ToHashSet();

            var pending = campaign.Recipients
                .Where(r => !sentSet.Contains(r.ContactId))
                .ToList();

            // Dedupe por dirección real: si dos contactos distintos comparten el mismo correo/teléfono,
            // que ya se le envió a esa dirección en esta campaña (por este canal) no debe volver a enviarse.
            var sentAddresses = await context.CommunicationLogs
                .Where(l => l.CampaignId == campaign.Id && l.Channel == effectiveChannel && l.Status == "Sent")
                .Select(l => l.Recipient)
                .ToListAsync();
            var seenAddresses = new HashSet<string>(
                sentAddresses.Where(a => !string.IsNullOrWhiteSpace(a)),
                StringComparer.OrdinalIgnoreCase);

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

            logger.LogInformation("Campaña {CampaignId}: iniciando envío por {Channel} a {Count} destinatario(s)",
                campaign.Id, effectiveChannel, pending.Count);

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

                if (seenAddresses.Contains(realAddress))
                {
                    // Mismo destino real que otro contacto ya cubierto en esta campaña/canal: no se reenvía.
                    recipient.Status = "Sent";
                    recipient.SentAt = DateTime.UtcNow;
                    recipient.ErrorMessage = null;
                    context.CommunicationLogs.Add(new CommunicationLog
                    {
                        CampaignId = campaign.Id,
                        ContactId = recipient.ContactId,
                        Channel = effectiveChannel,
                        Recipient = realAddress,
                        Status = "Sent",
                        ProviderResponse = "Duplicado: mismo destino que otro contacto en esta campaña",
                        CreatedAt = DateTime.UtcNow
                    });
                    await context.SaveChangesAsync();
                    continue;
                }

                // En Modo Prueba se redirige el destino real al de prueba y se marca el contenido.
                // El check de HTML se hace ANTES de anteponer el aviso de prueba, porque si no,
                // el texto ya no empieza con "<" y se trataría como texto plano (se escaparía el HTML).
                var isHtmlMessage = LooksLikeHtml(text);
                var destination = sending.TestMode ? testTarget : realAddress;
                if (sending.TestMode)
                {
                    text = isHtmlMessage
                        ? $"<div style='background:#fff3cd;color:#7a5b00;padding:10px 14px;font-family:Arial,sans-serif;font-size:13px;border-radius:8px;margin-bottom:14px;'>[PRUEBA → {System.Net.WebUtility.HtmlEncode(realAddress)}]</div>{text}"
                        : $"[PRUEBA → {realAddress}]\n\n{text}";
                }

                // Demora entre mensajes para no exceder los límites del proveedor (no se aplica
                // antes del primer envío). WhatsApp: 30-90s para no verse como mensajería masiva.
                // Email: pausa configurable (Sending:EmailDelay*) para no disparar el límite
                // anti-spam del buzón SMTP, que rechaza con "Sending limit reached" si se envía
                // demasiado rápido (Namecheap ~300/hora).
                if (!isFirstSend)
                {
                    if (isWhatsApp)
                    {
                        await whatsAppService.DelayBeforeNextSendAsync();
                    }
                    else if (isEmail)
                    {
                        var min = Math.Max(0, sending.EmailDelayMinMs);
                        var max = Math.Max(min, sending.EmailDelayMaxMs);
                        var delayMs = min == max ? min : Random.Shared.Next(min, max + 1);
                        if (delayMs > 0) await Task.Delay(delayMs);
                    }
                }
                isFirstSend = false;

                string status = "Failed";
                string? error = null;
                string? providerResponse = null;

                // Hasta 2 intentos por destinatario: los fallos transitorios (red, timeout SMTP,
                // 5xx del gateway) se reintentan una vez tras ~30s. Los errores permanentes
                // (HTTP 4xx, ej. 413 archivo grande) no se reintentan.
                const int maxAttempts = 2;
                for (var attempt = 1; attempt <= maxAttempts; attempt++)
                {
                    try
                    {
                        if (isEmail)
                        {
                            var html = isHtmlMessage ? text : TextToHtml(text);
                            var subject = sending.TestMode ? $"[PRUEBA] {campaign.Subject ?? campaign.Name}" : (campaign.Subject ?? campaign.Name);
                            await emailService.SendAsync(destination, displayName, subject, html, emailAttachments);
                            status = "Sent";
                            error = null;
                        }
                        else
                        {
                            var result = await whatsAppService.SendTextAsync(destination, text, firstFile);
                            providerResponse = result.ProviderResponse;
                            if (result.Success)
                            {
                                status = "Sent";
                                error = null;
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

                    if (status == "Sent") break;

                    var permanent = error != null && error.StartsWith("HTTP 4", StringComparison.OrdinalIgnoreCase);
                    if (permanent || attempt == maxAttempts) break;

                    logger.LogWarning("Campaña {CampaignId}: fallo transitorio enviando a {Recipient} ({Error}); reintentando en 30s (intento {Attempt}/{Max})",
                        campaign.Id, destination, error, attempt, maxAttempts);
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }

                if (status == "Sent")
                {
                    recipient.Status = "Sent";
                    recipient.SentAt = DateTime.UtcNow;
                    recipient.ErrorMessage = null;
                    sent++;
                    seenAddresses.Add(realAddress);
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

            logger.LogInformation("Campaña {CampaignId}: envío por {Channel} terminado. Enviados {Sent}, fallidos {Failed}",
                campaign.Id, effectiveChannel, sent, failed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error procesando el envío de la campaña {CampaignId}", campaignId);
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
                logger.LogError(saveEx, "No se pudo marcar como fallida la campaña {CampaignId} tras un error", campaignId);
            }
        }
        finally
        {
            ActiveSends.TryRemove(campaignId, out _);
        }
    }

    public static string RenderText(string template, Contact? contact, string eventName)
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

    public static string TextToHtml(string text)
    {
        // Texto plano -> HTML simple, respetando saltos de línea.
        return $"<div style='font-family:Arial,sans-serif;font-size:15px;color:#333;white-space:pre-wrap;'>{System.Net.WebUtility.HtmlEncode(text).Replace("\n", "<br>")}</div>";
    }

    public static bool LooksLikeHtml(string text) => text.TrimStart().StartsWith("<");
}
