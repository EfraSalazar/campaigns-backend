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
}
