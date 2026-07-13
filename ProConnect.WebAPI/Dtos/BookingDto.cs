using System;

namespace ProConnect.WebAPI.Dtos
{
    public class BookingDto
    {
        public int Id { get; set; }
        public int JobId { get; set; }
        public string JobTitle { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string VendorName { get; set; } = string.Empty;
        public string VendorCompany { get; set; } = string.Empty;
        public DateTime ScheduledDate { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal TotalPrice { get; set; }
        public bool IsPaid { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool HasReview { get; set; }
    }
}
