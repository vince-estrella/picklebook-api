using System.Net;
using System.Net.Mail;
using PickleballApi.Models;

namespace PickleballApi.Services
{
    public interface IEmailService
    {
        Task SendBookingReceiptAsync(Booking booking, Court court);
    }

    // Sends booking receipts over Gmail SMTP. Credentials come from env vars
    // so nothing secret lives in source control:
    //   SMTP_EMAIL         - the Gmail address to send from
    //   SMTP_APP_PASSWORD  - a Google Account "App Password" (not the login password)
    // If either is missing, we skip sending instead of throwing, so a missing
    // config never breaks booking creation or the Xendit webhook.
    public class EmailService : IEmailService
    {
        public async Task SendBookingReceiptAsync(Booking booking, Court court)
        {
            if (string.IsNullOrWhiteSpace(booking.BookerEmail)) return;

            var smtpEmail = Environment.GetEnvironmentVariable("SMTP_EMAIL");
            var smtpPassword = Environment.GetEnvironmentVariable("SMTP_APP_PASSWORD");

            if (string.IsNullOrEmpty(smtpEmail) || string.IsNullOrEmpty(smtpPassword))
            {
                Console.WriteLine("SMTP_EMAIL/SMTP_APP_PASSWORD not set — skipping booking receipt email.");
                return;
            }

            var hours = (decimal)(booking.EndTime - booking.StartTime).TotalHours;
            var amount = hours * court.PricePerHour;

            using var message = new MailMessage
            {
                From = new MailAddress(smtpEmail, "PickleBook"),
                Subject = $"Your booking receipt — {court.Name}",
                Body = BuildHtmlBody(booking, court, amount),
                IsBodyHtml = true,
            };
            message.To.Add(booking.BookerEmail);

            using var client = new SmtpClient("smtp.gmail.com", 587)
            {
                Credentials = new NetworkCredential(smtpEmail, smtpPassword),
                EnableSsl = true,
            };

            try
            {
                await client.SendMailAsync(message);
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
