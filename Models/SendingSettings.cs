namespace EventCampaignSystem.Models;

public class SendingSettings
{
    /// <summary>
    /// Cuando es true (local/dev), todos los envíos se redirigen a TestEmail/TestPhone
    /// y se aplica un tope de seguridad. En producción debe ser false.
    /// </summary>
    public bool TestMode { get; set; } = false;

    public string TestEmail { get; set; } = string.Empty;

    public string TestPhone { get; set; } = string.Empty;

    /// <summary>
    /// Tope de destinatarios procesados por envío cuando TestMode está activo.
    /// </summary>
    public int MaxRecipientsInTestMode { get; set; } = 5;

    /// <summary>
    /// Demora (ms) entre correos para no exceder el límite anti-spam del buzón SMTP.
    /// El buzón de Namecheap Private Email rechaza con "Sending limit reached" alrededor
    /// de ~300 correos/hora; con 14-18s entre envíos quedamos en ~200-250/hora.
    /// Para cada pausa se elige un valor aleatorio entre Min y Max. 0 = sin demora.
    /// </summary>
    public int EmailDelayMinMs { get; set; } = 14000;

    public int EmailDelayMaxMs { get; set; } = 18000;
}
