using EventCampaignSystem.Data;
using EventCampaignSystem.DTOs;
using EventCampaignSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EventCampaignSystem.Services;

public class ContactQueryService
{
    private readonly CampaignDbContext _context;

    public ContactQueryService(CampaignDbContext context)
    {
        _context = context;
    }

    public IQueryable<Contact> BuildQuery(ContactFilterRequest filter)
    {
        // Sin Include(Registrations): quien necesite los conteos usa ProjectToResponse
        // (subquery en SQL); materializar todos los registros de cada contacto era la
        // fuente principal de carga en previews de hasta 2000 contactos.
        var query = _context.Contacts.AsQueryable();

        if (!string.IsNullOrWhiteSpace(filter.Search))
        {
            var search = filter.Search.Trim();
            query = query.Where(c =>
                c.FirstName.Contains(search) ||
                c.LastName.Contains(search) ||
                c.SecondLastName.Contains(search) ||
                c.Email.Contains(search) ||
                c.PhoneNumber.Contains(search) ||
                c.Church.Contains(search));
        }

        if (!string.IsNullOrWhiteSpace(filter.City))
        {
            var city = filter.City.Trim();
            query = query.Where(c => c.City.Contains(city));
        }

        if (!string.IsNullOrWhiteSpace(filter.State))
        {
            var state = filter.State.Trim();
            query = query.Where(c => c.State.Contains(state));
        }

        if (!string.IsNullOrWhiteSpace(filter.Church))
        {
            var church = filter.Church.Trim();
            query = query.Where(c => c.Church.Contains(church));
        }

        if (filter.EventId.HasValue)
        {
            query = query.Where(c => c.Registrations.Any(r => r.EventId == filter.EventId.Value));
        }

        // Omitir a quienes YA se registraron a un evento (p.ej. no invitar a registrarse a quien ya lo hizo).
        if (filter.ExcludeRegisteredEventId.HasValue)
        {
            var excludeEventId = filter.ExcludeRegisteredEventId.Value;
            query = query.Where(c => !c.Registrations.Any(r => r.EventId == excludeEventId));
        }

        if (filter.RequireConsent && !string.IsNullOrWhiteSpace(filter.ConsentPurpose) && !string.IsNullOrWhiteSpace(filter.ConsentChannel))
        {
            var purpose = filter.ConsentPurpose.Trim();
            var channel = filter.ConsentChannel.Trim();
            query = query.Where(c => c.Consents.Any(consent =>
                consent.Purpose == purpose &&
                consent.Channel == channel &&
                consent.Accepted));
        }

        return query;
    }

    /// <summary>
    /// Proyección a ContactResponse traducida a SQL (los conteos salen como subqueries,
    /// sin cargar los registros de cada contacto en memoria).
    /// </summary>
    public static IQueryable<ContactResponse> ProjectToResponse(IQueryable<Contact> query)
    {
        return query.Select(c => new ContactResponse
        {
            Id = c.Id,
            FullName = (c.FirstName + " " + c.LastName + " " + c.SecondLastName).Trim(),
            Email = c.Email,
            PhoneNumber = c.PhoneNumber,
            City = c.City,
            State = c.State,
            Church = c.Church,
            RegistrationCount = c.Registrations.Count,
            LastRegistrationDate = c.Registrations
                .OrderByDescending(r => r.RegistrationDate)
                .Select(r => (DateTime?)r.RegistrationDate)
                .FirstOrDefault()
        });
    }

    public static ContactResponse ToResponse(Contact contact)
    {
        return new ContactResponse
        {
            Id = contact.Id,
            FullName = $"{contact.FirstName} {contact.LastName} {contact.SecondLastName}".Trim(),
            Email = contact.Email,
            PhoneNumber = contact.PhoneNumber,
            City = contact.City,
            State = contact.State,
            Church = contact.Church,
            RegistrationCount = contact.Registrations.Count,
            LastRegistrationDate = contact.Registrations
                .OrderByDescending(r => r.RegistrationDate)
                .Select(r => (DateTime?)r.RegistrationDate)
                .FirstOrDefault()
        };
    }
}
