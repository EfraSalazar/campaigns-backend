# Historial de despliegue campaigns

## 2026-06-13 - Despliegue inicial a VPS IONOS

Modulo de campanas desplegado a produccion en el VPS IONOS (`198.251.74.66`), junto al registro.

### Backend (.NET 8)
- Ruta VPS: `/var/www/event-campaign-api`
- Servicio systemd: `event-campaign-api` (mirror de `event-registration-api`)
- Puerto interno: `http://127.0.0.1:5001`
- Publish framework-dependent subido por rsync (el VPS solo tiene runtime, no SDK).
- Secretos en `/etc/event-campaign-api.env` (chmod 600), extraidos del appsettings de
  registro en el servidor: `EVENT_CAMPAIGN_CONNECTION`, `EmailSettings__Password`,
  `WhatsAppSettings__ApiKey`, `Jwt__Key`. WhatsApp Remitente se queda en `tangente`.
- `Sending__TestMode=true` (redirige a efras.salazar@gmail.com / 527771619340).

### Frontend (Vue/Vite)
- Ruta VPS: `/var/www/intimosconf/campanas`
- URL publica: `https://intimosconf.com/campanas/`
- Build con `--base=/campanas/` y `VITE_API_BASE_URL=https://intimosconf.com/campanas`.

### Nginx
- Se agregaron a `intimosconf.com`: `location /campanas/` (estatico) y
  `location /campanas/api/` (proxy a 5001, client_max_body_size 20m).
- Backup previo: `/var/www/backups/nginx-intimosconf.com-<ts>.bak`.
- `nginx -t` OK, reload sin afectar `/registro/` ni `/api/`.

### Base de datos
- BD de produccion `BdEventRegistration` (misma del registro, SQL Server systemd).
- Tablas de campana creadas con `sql/create_campaign_tables.sql` (idempotente).
- Login usa la tabla `Users` compartida: admin/asistente. La password de `admin` es la
  REAL de produccion (no `admin1`). Solo rol Admin entra.

### Pendiente operativo
- Probar login + envio en TestMode (llega a correo/telefono propios).
- Cuando se confirme, cambiar `Sending__TestMode=false` en `/etc/event-campaign-api.env`
  y `systemctl restart event-campaign-api` para envio real.

## 2026-06-13 - Mejoras UX (solo frontend)

- Alerts con SweetAlert2 (libreria que ya usa el registro): modales de
  confirmacion/input/exito y toasts, reemplazando window.prompt/confirm/banners.
- Pestana Revision reorganizada en 3 pasos numerados: 1) Elegir destinatarios,
  2) Preparar (adjuntos), 3) Enviar. Los botones "Agregar" quedan junto a la lista y
  el envio se separa al final.
- Solo cambio frontend. Build `npm run build -- --base=/campanas/`, backup previo en
  `/var/www/backups/campanas-<ts>`, rsync de `dist/` a `/var/www/intimosconf/campanas/`.
- Verificado: panel 200, asset 200, swal2 en bundle, Health ok, TestMode sigue true.
