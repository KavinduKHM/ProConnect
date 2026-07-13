using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using ProConnect.Domain.Entities;
using ProConnect.Infrastructure.Data;
using ProConnect.WebAPI.Dtos;
using ProConnect.WebAPI.Hubs;

namespace ProConnect.WebAPI.Services
{
    /// <summary>Persists a notification and pushes it to the user over SignalR.</summary>
    public class NotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHubContext<NotificationHub> _hubContext;

        public NotificationService(ApplicationDbContext context, IHubContext<NotificationHub> hubContext)
        {
            _context = context;
            _hubContext = hubContext;
        }

        /// <param name="userId">The Identity user id (ApplicationUser.Id), not a profile id.</param>
        public async Task NotifyUserAsync(
            string userId,
            string title,
            string message,
            string? actionUrl = null,
            CancellationToken cancellationToken = default)
        {
            var notification = new Notification
            {
                UserId = userId,
                Title = title,
                Message = message,
                ActionUrl = actionUrl,
                CreatedAt = DateTime.UtcNow,
                IsRead = false
            };

            _context.Notifications.Add(notification);
            await _context.SaveChangesAsync(cancellationToken);

            var dto = new NotificationDto
            {
                Id = notification.Id,
                Title = notification.Title,
                Message = notification.Message,
                ActionUrl = notification.ActionUrl,
                CreatedAt = notification.CreatedAt,
                IsRead = false
            };

            await _hubContext.Clients.User(userId).SendAsync("NewNotification", dto, cancellationToken);
        }
    }
}
