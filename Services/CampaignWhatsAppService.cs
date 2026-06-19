using System.Text;
using System.Text.Json;
using EventCampaignSystem.Models;
using Microsoft.Extensions.Options;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace EventCampaignSystem.Services;

public class CampaignWhatsAppService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly WhatsAppSettings _settings;
    private readonly ILogger<CampaignWhatsAppService> _logger;

    public CampaignWhatsAppService(
        IHttpClientFactory httpClientFactory,
        IOptions<WhatsAppSettings> settings,
        ILogger<CampaignWhatsAppService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_settings.BaseUrl) &&
        !string.IsNullOrWhiteSpace(_settings.ApiKey) &&
        !string.IsNullOrWhiteSpace(_settings.Remitente);

    // Espera aleatoria entre envíos consecutivos para que una campaña no se vea como mensajería masiva ante WhatsApp.
    public Task DelayBeforeNextSendAsync()
    {
        var min = Math.Max(0, _settings.DelayMinMs);
        var max = Math.Max(min, _settings.DelayMaxMs);
        var delayMs = min == max ? min : Random.Shared.Next(min, max + 1);
        return delayMs > 0 ? Task.Delay(delayMs) : Task.CompletedTask;
    }

    public class SendResult
    {
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? ProviderResponse { get; set; }
    }

    public record FileAttachment(string FileName, string ContentType, byte[] Content);

    public async Task<SendResult> SendTextAsync(string phoneNumber, string message, FileAttachment? file = null)
    {
        if (!IsConfigured)
        {
            return new SendResult { Success = false, Error = "Configuración de WhatsApp (WhatsAppSettings) incompleta." };
        }

        if (file != null) file = CompressIfNeeded(file);

        object payload = file == null
            ? new
            {
                Remitente = _settings.Remitente,
                Destinatario = phoneNumber,
                Mensaje = message
            }
            : new
            {
                Remitente = _settings.Remitente,
                Destinatario = phoneNumber,
                Mensaje = message,
                Archivo = new
                {
                    Nombre = file.FileName,
                    MimeType = file.ContentType,
                    BinarioHex = Convert.ToHexString(file.Content)
                }
            };

        try
        {
            var payloadJson = JsonSerializer.Serialize(payload);
            var client = _httpClientFactory.CreateClient("WhatsAppApi");
            using var request = new HttpRequestMessage(HttpMethod.Post, string.Empty);
            request.Headers.Add("X-API-Key", _settings.ApiKey);
            request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError("Error HTTP al enviar WhatsApp a {Phone}: {Code}", phoneNumber, response.StatusCode);
                return new SendResult { Success = false, Error = $"HTTP {(int)response.StatusCode}", ProviderResponse = Trim(responseContent) };
            }

            if (!IsProviderResponseSuccessful(responseContent))
            {
                return new SendResult { Success = false, Error = GetProviderError(responseContent), ProviderResponse = Trim(responseContent) };
            }

            _logger.LogInformation("WhatsApp enviado a {Phone}", phoneNumber);
            return new SendResult { Success = true, ProviderResponse = Trim(responseContent) };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Excepción al enviar WhatsApp a {Phone}", phoneNumber);
            return new SendResult { Success = false, Error = ex.Message };
        }
    }

    private const int MaxFileSizeBytes = 800 * 1024; // 800 KB

    private FileAttachment CompressIfNeeded(FileAttachment file)
    {
        if (file.Content.Length <= MaxFileSizeBytes) return file;

        var isImage = file.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
            && !file.ContentType.Contains("gif", StringComparison.OrdinalIgnoreCase)
            && !file.ContentType.Contains("svg", StringComparison.OrdinalIgnoreCase);
        if (!isImage) return file;

        try
        {
            using var img = Image.Load(file.Content);

            // Escala si es muy grande
            if (img.Width > 1280 || img.Height > 1280)
                img.Mutate(x => x.Resize(new ResizeOptions { Size = new Size(1280, 1280), Mode = ResizeMode.Max }));

            // Comprime en calidad descendente hasta caber
            for (int quality = 75; quality >= 40; quality -= 15)
            {
                using var ms = new MemoryStream();
                img.Save(ms, new JpegEncoder { Quality = quality });
                if (ms.Length <= MaxFileSizeBytes || quality == 40)
                {
                    var compressed = ms.ToArray();
                    _logger.LogInformation("Imagen comprimida: {Before} KB → {After} KB (q{Q})",
                        file.Content.Length / 1024, compressed.Length / 1024, quality);
                    return new FileAttachment(
                        Path.ChangeExtension(file.FileName, ".jpg"),
                        "image/jpeg",
                        compressed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo comprimir la imagen, se envía sin comprimir");
        }

        return file;
    }

    private static string Trim(string s) => string.IsNullOrEmpty(s) ? s : (s.Length > 500 ? s[..500] : s);

    private static bool IsProviderResponseSuccessful(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent)) return true;
        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return true;

            if (root.TryGetProperty("ok", out var ok) && ok.ValueKind == JsonValueKind.False) return false;
            foreach (var name in new[] { "Type", "type" })
            {
                if (root.TryGetProperty(name, out var t) && t.ValueKind == JsonValueKind.String)
                {
                    var v = t.GetString() ?? string.Empty;
                    if (v.Equals("warning", StringComparison.OrdinalIgnoreCase) || v.Equals("error", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            foreach (var name in new[] { "Number", "number" })
            {
                if (root.TryGetProperty(name, out var n) && n.ValueKind == JsonValueKind.Number && n.TryGetInt32(out var num) && num < 0)
                    return false;
            }
            if (root.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(err.GetString()))
                return false;
        }
        catch (JsonException)
        {
            return true;
        }
        return true;
    }

    private static string GetProviderError(string responseContent)
    {
        if (string.IsNullOrWhiteSpace(responseContent)) return "Respuesta vacía del proveedor";
        try
        {
            using var doc = JsonDocument.Parse(responseContent);
            var root = doc.RootElement;
            foreach (var name in new[] { "message", "error" })
            {
                if (root.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(p.GetString()))
                    return p.GetString()!;
            }
        }
        catch (JsonException) { }
        return Trim(responseContent);
    }
}
