using System.Text;
using EventCampaignSystem.Data;
using EventCampaignSystem.Models;
using EventCampaignSystem.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var connectionString = Environment.GetEnvironmentVariable("EVENT_CAMPAIGN_CONNECTION");
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
}

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("Configura ConnectionStrings:DefaultConnection o la variable EVENT_CAMPAIGN_CONNECTION.");
}

builder.Services.AddDbContext<CampaignDbContext>(options =>
    options.UseSqlServer(connectionString, sqlOptions =>
    {
        sqlOptions.EnableRetryOnFailure(
            maxRetryCount: 5,
            maxRetryDelay: TimeSpan.FromSeconds(30),
            errorNumbersToAdd: null);
    }));

builder.Services.AddScoped<ContactQueryService>();

builder.Services.Configure<EmailSettings>(builder.Configuration.GetSection("EmailSettings"));
builder.Services.AddScoped<CampaignEmailService>();

builder.Services.Configure<SendingSettings>(builder.Configuration.GetSection("Sending"));

builder.Services.Configure<WhatsAppSettings>(builder.Configuration.GetSection("WhatsAppSettings"));
builder.Services.AddScoped<CampaignWhatsAppService>();
builder.Services.AddHttpClient("WhatsAppApi", client =>
{
    var baseUrl = builder.Configuration["WhatsAppSettings:BaseUrl"];
    if (!string.IsNullOrWhiteSpace(baseUrl))
    {
        client.BaseAddress = new Uri(baseUrl);
    }
});

builder.Services.AddScoped<AuthService>();

// Singleton: mantiene el registro de envíos activos del proceso; crea sus propios scopes.
builder.Services.AddSingleton<CampaignSendService>();
// Worker de campañas programadas (revisa cada minuto las Status="Scheduled" vencidas).
builder.Services.AddHostedService<ScheduledCampaignWorker>();

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey))
{
    throw new InvalidOperationException("Configura Jwt:Key (o la variable Jwt__Key) para la autenticación.");
}

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
});

// Orígenes permitidos por config (Cors:AllowedOrigins). En producción el panel se sirve
// desde el mismo dominio, pero se restringe igualmente para que ningún otro sitio pueda
// llamar la API desde un navegador con las cookies/tokens del admin.
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? new[] { "https://intimosconf.com" };
builder.Services.AddCors(options =>
{
    options.AddPolicy("LocalCampaignAdmin", policy =>
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod());
});

var app = builder.Build();

// Recuperar campañas huérfanas: si el servicio se reinició a media campaña, la tarea de envío
// murió y el estado quedó en "Sending". Se resetean a "Draft" para poder reanudarlas; la
// deduplicación por CommunicationLogs garantiza que al re-enviar no se duplica a quien ya recibió.
using (var startupScope = app.Services.CreateScope())
{
    try
    {
        var db = startupScope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        var recovered = await db.Campaigns
            .Where(c => c.Status == "Sending")
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, "Draft"));
        if (recovered > 0)
        {
            app.Logger.LogWarning("Se recuperaron {Count} campaña(s) atorada(s) en 'Sending' (reseteadas a 'Draft')", recovered);
        }
    }
    catch (Exception ex)
    {
        // No impedir el arranque si la BD aún no está lista; el rescate por inactividad (>5 min) sigue disponible.
        app.Logger.LogError(ex, "No se pudieron recuperar campañas atoradas al arrancar");
    }
}

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseRouting();
app.UseCors("LocalCampaignAdmin");
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
