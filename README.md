# EventCampaignSystem

Modulo separado para preparar campanas de comunicacion usando la base historica de INTIMOS.

## Objetivo

- Leer contactos historicos desde `Contacts`.
- Consultar historial por evento desde `Registrations` y `Events`.
- Crear campanas para INTIMOS o Instituto Teologico sin afectar el flujo de registro.
- Registrar destinatarios, consentimientos y bitacoras de comunicacion en tablas propias.

## Seguridad operativa

Este proyecto no crea tablas automaticamente. Antes de usar campanas en la base real, ejecutar y verificar:

```bash
sql/create_campaign_tables.sql
```

No desplegar publicamente sin autenticacion.

## Conexion

Configurar la cadena con variable de entorno para no copiar secretos:

```bash
export EVENT_CAMPAIGN_CONNECTION='Server=localhost,1433;Database=BdEventRegistration;User Id=appevent;Password=***;Encrypt=True;TrustServerCertificate=True;'
dotnet run
```

## Endpoints iniciales

- `GET /api/Health`
- `GET /api/Contacts`
- `GET /api/Contacts/{id}/registrations`
- `GET /api/Events`
- `GET /api/Events/active`
- `GET /api/Campaigns`
- `POST /api/Campaigns`
- `POST /api/Campaigns/{id}/recipients/preview`
- `POST /api/Campaigns/{id}/recipients/from-filter`
- `GET /api/Campaigns/{id}/recipients`
- `GET /api/Consents/contact/{contactId}`
- `PUT /api/Consents/contact/{contactId}`

## Finalidades sugeridas

- `IntimosEvents`
- `TheologicalInstitute`
- `GeneralMinistry`

## Canales sugeridos

- `WhatsApp`
- `Email`
