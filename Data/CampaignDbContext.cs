using EventCampaignSystem.Models;
using Microsoft.EntityFrameworkCore;

namespace EventCampaignSystem.Data;

public class CampaignDbContext : DbContext
{
    public CampaignDbContext(DbContextOptions<CampaignDbContext> options) : base(options)
    {
    }

    public DbSet<Contact> Contacts { get; set; }
    public DbSet<Event> Events { get; set; }
    public DbSet<Registration> Registrations { get; set; }
    public DbSet<Campaign> Campaigns { get; set; }
    public DbSet<CampaignRecipient> CampaignRecipients { get; set; }
    public DbSet<CommunicationLog> CommunicationLogs { get; set; }
    public DbSet<ContactConsent> ContactConsents { get; set; }
    public DbSet<ContactTag> ContactTags { get; set; }
    public DbSet<ContactTagAssignment> ContactTagAssignments { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<CampaignAttachment> CampaignAttachments { get; set; }
    public DbSet<SavedSegment> SavedSegments { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Contact>().ToTable("Contacts");
        modelBuilder.Entity<Event>().ToTable("Events");
        modelBuilder.Entity<Registration>().ToTable("Registrations");
        modelBuilder.Entity<User>().ToTable("Users");

        modelBuilder.Entity<CampaignAttachment>()
            .HasOne(a => a.Campaign)
            .WithMany()
            .HasForeignKey(a => a.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Contact>()
            .HasMany(c => c.Registrations)
            .WithOne(r => r.Contact)
            .HasForeignKey(r => r.ContactId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Event>()
            .HasMany(e => e.Registrations)
            .WithOne(r => r.Event)
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Campaign>()
            .HasIndex(c => new { c.Purpose, c.Channel, c.Status });

        modelBuilder.Entity<CampaignRecipient>()
            .HasIndex(r => new { r.CampaignId, r.ContactId })
            .IsUnique();

        modelBuilder.Entity<CampaignRecipient>()
            .HasOne(r => r.Campaign)
            .WithMany(c => c.Recipients)
            .HasForeignKey(r => r.CampaignId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CampaignRecipient>()
            .HasOne(r => r.Contact)
            .WithMany(c => c.CampaignRecipients)
            .HasForeignKey(r => r.ContactId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CampaignRecipient>()
            .HasOne(r => r.SourceEvent)
            .WithMany()
            .HasForeignKey(r => r.SourceEventId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<ContactConsent>()
            .HasIndex(c => new { c.ContactId, c.Purpose, c.Channel })
            .IsUnique();

        modelBuilder.Entity<ContactConsent>()
            .HasOne(c => c.Contact)
            .WithMany(c => c.Consents)
            .HasForeignKey(c => c.ContactId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CommunicationLog>()
            .HasOne(l => l.Campaign)
            .WithMany()
            .HasForeignKey(l => l.CampaignId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<CommunicationLog>()
            .HasOne(l => l.Contact)
            .WithMany()
            .HasForeignKey(l => l.ContactId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<ContactTag>()
            .HasIndex(t => t.Name)
            .IsUnique();

        modelBuilder.Entity<ContactTagAssignment>()
            .HasKey(a => new { a.ContactId, a.ContactTagId });

        modelBuilder.Entity<ContactTagAssignment>()
            .HasOne(a => a.Contact)
            .WithMany()
            .HasForeignKey(a => a.ContactId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<ContactTagAssignment>()
            .HasOne(a => a.ContactTag)
            .WithMany(t => t.Assignments)
            .HasForeignKey(a => a.ContactTagId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
