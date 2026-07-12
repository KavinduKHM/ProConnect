using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace ProConnect.WebAPI.Hubs
{
    [Authorize] // Require authenticated user
    public class NotificationHub : Hub
    {
        // Override OnConnectedAsync to log or handle connection
        public override async Task OnConnectedAsync()
        {
            var user = Context.User;
            Console.WriteLine($"[SignalR] Client connected: {Context.ConnectionId}");
            if (user != null)
            {
                Console.WriteLine($"[SignalR] User identity: {user.Identity?.Name}, IsAuthenticated: {user.Identity?.IsAuthenticated}");
                Console.WriteLine($"[SignalR] UserIdentifier: {Context.UserIdentifier}");
                Console.WriteLine($"[SignalR] Has Vendor Role? {user.IsInRole("Vendor")}");
                if (user.IsInRole("Vendor"))
                {
                    await Groups.AddToGroupAsync(Context.ConnectionId, "Vendors");
                    Console.WriteLine($"[SignalR] Added {Context.ConnectionId} to Vendors group");
                }
            }
            else
            {
                Console.WriteLine("[SignalR] User is null.");
            }
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            var user = Context.User;
            if (user != null && user.IsInRole("Vendor"))
            {
                await Groups.RemoveFromGroupAsync(Context.ConnectionId, "Vendors");
            }
            await base.OnDisconnectedAsync(exception);
        }
    }
}