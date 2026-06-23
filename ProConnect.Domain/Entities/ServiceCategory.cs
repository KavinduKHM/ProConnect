namespace ProConnect.Domain.Entities
{
    public class ServiceCategory
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty; // e.g., "Plumbing", "Electrical", "Painting"
        public string? Description { get; set; }
        public string? IconUrl { get; set; } // For frontend display
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; }

        // Navigation property: One Category has many Jobs
        public ICollection<Job> Jobs { get; set; } = new List<Job>();
        public ICollection<VendorProfile> Vendors { get; set; } = new List<VendorProfile>();
    }
}