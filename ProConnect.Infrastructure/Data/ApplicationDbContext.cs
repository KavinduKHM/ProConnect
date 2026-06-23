using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ProConnect.Domain.Entities;

namespace ProConnect.Infrastructure.Data
{
    public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<ServiceCategory> ServiceCategories { get; set; }
        public DbSet<VendorProfile> VendorProfiles { get; set; }
        public DbSet<CustomerProfile> CustomerProfiles { get; set; }
        public DbSet<Job> Jobs { get; set; }
        public DbSet<JobBid> JobBids { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Review> Reviews { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            base.OnModelCreating(builder);

            // Configure 1:1 relationship between ApplicationUser and CustomerProfile
            builder.Entity<CustomerProfile>()
                .HasOne(c => c.User)
                .WithOne()
                .HasForeignKey<CustomerProfile>(c => c.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Configure 1:1 relationship between ApplicationUser and VendorProfile
            builder.Entity<VendorProfile>()
                .HasOne(v => v.User)
                .WithOne()
                .HasForeignKey<VendorProfile>(v => v.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Many-to-Many: VendorProfile <-> ServiceCategory
            builder.Entity<VendorProfile>()
                .HasMany(v => v.ServiceCategories)
                .WithMany(c => c.Vendors)
                .UsingEntity(j => j.ToTable("VendorServiceCategories"));

            // Job -> ServiceCategory
            builder.Entity<Job>()
                .HasOne(j => j.ServiceCategory)
                .WithMany(c => c.Jobs)
                .HasForeignKey(j => j.ServiceCategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // Job -> CustomerProfile
            builder.Entity<Job>()
                .HasOne(j => j.Customer)
                .WithMany(c => c.Jobs)
                .HasForeignKey(j => j.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Job -> VendorProfile (Assigned Vendor)
            builder.Entity<Job>()
                .HasOne(j => j.AssignedVendor)
                .WithMany(v => v.Jobs)
                .HasForeignKey(j => j.VendorProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // JobBid -> Job
            builder.Entity<JobBid>()
                .HasOne(b => b.Job)
                .WithMany(j => j.Bids)
                .HasForeignKey(b => b.JobId)
                .OnDelete(DeleteBehavior.Cascade);

            // JobBid -> VendorProfile
            builder.Entity<JobBid>()
                .HasOne(b => b.Vendor)
                .WithMany(v => v.Bids)
                .HasForeignKey(b => b.VendorProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // Booking -> Job (1:1)
            builder.Entity<Booking>()
                .HasOne(b => b.Job)
                .WithOne(j => j.Booking)
                .HasForeignKey<Booking>(b => b.JobId)
                .OnDelete(DeleteBehavior.Cascade);

            // Booking -> CustomerProfile
            builder.Entity<Booking>()
                .HasOne(b => b.Customer)
                .WithMany(c => c.Bookings)
                .HasForeignKey(b => b.CustomerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Booking -> VendorProfile
            builder.Entity<Booking>()
                .HasOne(b => b.Vendor)
                .WithMany(v => v.Bookings)
                .HasForeignKey(b => b.VendorProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // Review -> Job (1:1)
            builder.Entity<Review>()
                .HasOne(r => r.Job)
                .WithOne(j => j.Review)
                .HasForeignKey<Review>(r => r.JobId)
                .OnDelete(DeleteBehavior.Cascade);

            // Review -> CustomerProfile (Reviewer)
            builder.Entity<Review>()
                .HasOne(r => r.Reviewer)
                .WithMany(c => c.ReviewsGiven)
                .HasForeignKey(r => r.ReviewerId)
                .OnDelete(DeleteBehavior.Restrict);

            // Review -> VendorProfile
            builder.Entity<Review>()
                .HasOne(r => r.Vendor)
                .WithMany(v => v.ReviewsReceived)
                .HasForeignKey(r => r.VendorProfileId)
                .OnDelete(DeleteBehavior.Restrict);

            // Seed Service Categories
            builder.Entity<ServiceCategory>().HasData(
                new ServiceCategory { Id = 1, Name = "Plumbing", Description = "Pipe repair, installation, and maintenance", IsActive = true },
                new ServiceCategory { Id = 2, Name = "Electrical", Description = "Wiring, installations, and electrical repairs", IsActive = true },
                new ServiceCategory { Id = 3, Name = "Painting", Description = "Interior and exterior painting services", IsActive = true },
                new ServiceCategory { Id = 4, Name = "Transport", Description = "Moving, delivery, and logistics services", IsActive = true },
                new ServiceCategory { Id = 5, Name = "Carpentry", Description = "Woodwork, furniture repair, and installations", IsActive = true },
                new ServiceCategory { Id = 6, Name = "Cleaning", Description = "Residential and commercial cleaning", IsActive = true },
                new ServiceCategory { Id = 7, Name = "HVAC", Description = "Heating, ventilation, and air conditioning", IsActive = true },
                new ServiceCategory { Id = 8, Name = "Gardening", Description = "Landscaping, lawn care, and gardening", IsActive = true }
            );
        }
    }
}