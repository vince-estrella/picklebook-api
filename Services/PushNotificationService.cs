using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using WebPush;

namespace PickleballApi.Services
{
    public record PushMessage(string Title, string Body, string Url, string Tag = "picklebook");

    public interface IPushNotificationService
    {
        string? PublicKey { get; }
        Task SendToPlayerAsync(int userId, PushMessage message);
        Task SendToOwnerAsync(int courtOwnerId, PushMessage message);
    }

    public class PushNotificationService : IPushNotificationService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly string? _publicKey;
        private readonly string? _privateKey;
        private readonly string _subject;

        public PushNotificationService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
            _publicKey = Environment.GetEnvironmentVariable("VAPID_PUBLIC_KEY");
            _privateKey = Environment.GetEnvironmentVariable("VAPID_PRIVATE_KEY");
            _subject = Environment.GetEnvironmentVariable("VAPID_SUBJECT") ?? "mailto:support@thepicklebook.app";
        }

        public string? PublicKey => string.IsNullOrWhiteSpace(_publicKey) ? null : _publicKey;

        public Task SendToPlayerAsync(int userId, PushMessage message) =>
            SendAsync(query => query.Where(s => s.UserRole == "Player" && s.UserId == userId), message);

        public Task SendToOwnerAsync(int courtOwnerId, PushMessage message) =>
            SendAsync(query => query.Where(s => s.UserRole == "CourtOwner" && s.CourtOwnerId == courtOwnerId), message);

        private async Task SendAsync(
            Func<IQueryable<Models.PushSubscription>, IQueryable<Models.PushSubscription>> filter,
            PushMessage message)
        {
            if (string.IsNullOrWhiteSpace(_publicKey) || string.IsNullOrWhiteSpace(_privateKey))
            {
                Console.WriteLine("VAPID keys are not set; skipping push notification.");
                return;
            }

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var subscriptions = await filter(db.PushSubscriptions).ToListAsync();
            if (subscriptions.Count == 0) return;

            var vapidDetails = new VapidDetails(_subject, _publicKey, _privateKey);
            var client = new WebPushClient();
            var payload = JsonSerializer.Serialize(new
            {
                title = message.Title,
                body = message.Body,
                url = message.Url,
                tag = message.Tag,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });

            foreach (var saved in subscriptions)
            {
                var subscription = new WebPush.PushSubscription(saved.Endpoint, saved.P256dh, saved.Auth);
                try
                {
                    await client.SendNotificationAsync(subscription, payload, vapidDetails);
                    saved.LastUsedAt = DateTime.UtcNow;
                }
                catch (WebPushException ex) when ((int?)ex.StatusCode is 404 or 410)
                {
                    db.PushSubscriptions.Remove(saved);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Push send failed for subscription {saved.Id}: {ex.Message}");
                }
            }

            await db.SaveChangesAsync();
        }
    }
}
