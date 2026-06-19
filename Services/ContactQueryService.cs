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
        var query = _context.Contacts
            .Include(c => c.Registrations)
            .AsQueryable();

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
