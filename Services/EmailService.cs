using System.Net;
using System.Net.Http.Json;
using PickleballApi.Models;

namespace PickleballApi.Services
{
    public interface IEmailService
    {
        Task SendBookingReceiptAsync(Booking booking, Court court);
    }

    // Sends booking receipts via Resend (https://resend.com). Credentials
    // come from env vars so nothing secret lives in source control:
    //   RESEND_API_KEY    - required, from the Resend dashboard.
    //   RESEND_FROM_EMAIL - optional. Defaults to Resend's shared test
    //                       sender, which can only deliver to the email
    //                       address the Resend account itself was signed up
    //                       with. Verify a domain in Resend and set this to
    //                       e.g. "PickleBook <receipts@yourdomain.com>" to
    //                       send receipts to real customers.
    // If RESEND_API_KEY is missing, we skip sending instead of throwing, so
    // a missing config never breaks booking creation or the Xendit webhook.
    public class EmailService : IEmailService
    {
        private readonly IHttpClientFactory _httpClientFactory;

        public EmailService(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
        }

        public async Task SendBookingReceiptAsync(Booking booking, Court court)
        {
            if (string.IsNullOrWhiteSpace(booking.BookerEmail)) return;

            var apiKey = Environment.GetEnvironmentVariable("RESEND_API_KEY");
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.WriteLine("RESEND_API_KEY not set — skipping booking receipt email.");
                return;
            }

            var fromEmail = Environment.GetEnvironmentVariable("RESEND_FROM_EMAIL") ?? "PickleBook <onboarding@resend.dev>";

            var hours = (decimal)(booking.EndTime - booking.StartTime).TotalHours;
            var amount = hours * court.PricePerHour;

            var payload = new
            {
                from = fromEmail,
                to = new[] { booking.BookerEmail },
                subject = $"Your booking receipt — {court.Name}",
                html = BuildHtmlBody(booking, court, amount)
            };

            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                var response = await client.PostAsJsonAsync("https://api.resend.com/emails", payload);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    Console.WriteLine($"Failed to send booking receipt email ({(int)response.StatusCode}): {body}");
                }
            }
            catch (Exception ex)
            {
                // A failed receipt email should never fail the booking itself.
                Console.WriteLine($"Failed to send booking receipt email: {ex.Message}");
            }
        }

        private static string BuildHtmlBody(Booking booking, Court court, decimal amount)
        {
            var dateLabel = booking.Date.ToString("MMMM d, yyyy");
            var timeLabel = $"{FormatTime(booking.StartTime)} – {FormatTime(booking.EndTime)}";
            var paymentLabel = booking.PaymentMethod == "Online"
                ? "Paid online"
                : "Pay at venue";

            return $@"
<div style=""font-family: Arial, sans-serif; max-width: 480px; margin: 0 auto; color: #101817;"">
  <h2 style=""color: #0F6B5C;"">Booking Receipt</h2>
  <p>Hi {WebUtility.HtmlEncode(booking.BookerName)},</p>
  <p>Here's your receipt for <strong>{WebUtility.HtmlEncode(court.Name)}</strong>.</p>
  <table style=""width: 100%; border-collapse: collapse; margin: 16px 0;"">
    <tr><td style=""padding: 6px 0; color: #5B6864;"">Booking Reference</td><td style=""padding: 6px 0; text-align: right; font-weight: bold;"">{WebUtility.HtmlEncode(booking.BookingReference)}</td></tr>
    <tr><td style=""padding: 6px 0; color: #5B6864;"">Court</td><td style=""padding: 6px 0; text-align: right;"">{WebUtility.HtmlEncode(court.Name)}</td></tr>
    <tr><td style=""padding: 6px 0; color: #5B6864;"">Address</td><td style=""padding: 6px 0; text-align: right;"">{WebUtility.HtmlEncode(court.Address)}</td></tr>
    <tr><td style=""padding: 6px 0; color: #5B6864;"">Date</td><td style=""padding: 6px 0; text-align: right;"">{dateLabel}</td></tr>
    <tr><td style=""padding: 6px 0; color: #5B6864;"">Time</td><td style=""padding: 6px 0; text-align: right;"">{timeLabel}</td></tr>
    <tr><td style=""padding: 6px 0; color: #5B6864;"">Payment</td><td style=""padding: 6px 0; text-align: right;"">{paymentLabel}</td></tr>
    <tr><td style=""padding: 10px 0 0; color: #101817; font-weight: bold; border-top: 1px solid #DCE1D6;"">Total</td><td style=""padding: 10px 0 0; text-align: right; font-weight: bold; border-top: 1px solid #DCE1D6;"">₱{amount:F2}</td></tr>
  </table>
  <p style=""color: #5B6864; font-size: 13px;"">Show this email or your booking reference at check-in. Thanks for booking with PickleBook!</p>
</div>";
        }

        private static string FormatTime(TimeSpan time)
        {
            var dt = DateTime.Today.Add(time);
            return dt.ToString("h:mm tt");
        }
    }
}
