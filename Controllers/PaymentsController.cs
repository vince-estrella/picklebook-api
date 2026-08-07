using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using PickleballApi.Services;
using System.Text.Json;

namespace PickleballApi.Controllers
{
    [ApiController]
    [Route("api/payments")]
    public class PaymentsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IEmailService _emailService;

        public PaymentsController(AppDbContext context, IEmailService emailService)
        {
            _context = context;
            _emailService = emailService;
        }

        [HttpPost("webhook")]
        public async Task<ActionResult> XenditWebhook()
        {
            var callbackToken = Request.Headers["x-callback-token"].ToString();
            var expectedToken = Environment.GetEnvironmentVariable("XENDIT_CALLBACK_TOKEN");

            if (string.IsNullOrEmpty(expectedToken) || callbackToken != expectedToken)
            {
                return Unauthorized();
            }

            using var reader = new StreamReader(Request.Body);
            var rawBody = await reader.ReadToEndAsync();

            JsonElement root;
            try
            {
                root = JsonDocument.Parse(rawBody).RootElement;
            }
            catch (JsonException ex)
            {
                Console.WriteLine($"Xendit webhook: could not parse payload as JSON: {ex.Message}");
                return Ok();
            }

            if (!root.TryGetProperty("status", out var statusProp) || !root.TryGetProperty("id", out var idProp))
            {
                Console.WriteLine("Xendit webhook: payload missing expected 'status' or 'id' field.");
                return Ok();
            }

            var status = statusProp.GetString();
            var invoiceId = idProp.GetString();

            if (status != "PAID")
            {
                return Ok();
            }

            var booking = await _context.Bookings
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.XenditInvoiceId == invoiceId);

            if (booking == null || booking.Court == null)
            {
                Console.WriteLine($"Xendit webhook: no booking found for invoice {invoiceId} (status PAID). Ignoring.");
                return Ok();
            }

            if (booking.PaymentStatus == "Paid")
            {
                return Ok();
            }

            var expectedHours = (decimal)(booking.EndTime - booking.StartTime).TotalHours;
            var expectedAmount = expectedHours * booking.Court.PricePerHour;

            decimal? paidAmount = null;
            if (root.TryGetProperty("paid_amount", out var paidAmountProp) && paidAmountProp.ValueKind == JsonValueKind.Number)
            {
                paidAmount = paidAmountProp.GetDecimal();
            }
            else if (root.TryGetProperty("amount", out var amountProp) && amountProp.ValueKind == JsonValueKind.Number)
            {
                paidAmount = amountProp.GetDecimal();
            }

            var currency = root.TryGetProperty("currency", out var currencyProp) ? currencyProp.GetString() : null;
            var amountMatches = paidAmount.HasValue && Math.Abs(paidAmount.Value - expectedAmount) < 1m;
            var currencyMatches = string.Equals(currency, "PHP", StringComparison.OrdinalIgnoreCase);

            if (!amountMatches || !currencyMatches)
            {
                Console.WriteLine(
                    $"Xendit webhook amount/currency mismatch for invoice {invoiceId} " +
                    $"(booking {booking.Id}): expected {expectedAmount} PHP, got " +
                    $"{(paidAmount.HasValue ? paidAmount.Value.ToString() : "null")} {currency ?? "null"}. " +
                    "Booking NOT marked paid.");
                return Ok();
            }

            if (booking.Status == "Cancelled")
            {
                bool slotTakenByAnother = await _context.Bookings.AnyAsync(b =>
                    b.Id != booking.Id &&
                    b.CourtId == booking.CourtId &&
                    b.Date.Date == booking.Date.Date &&
                    b.Status != "Cancelled" &&
                    booking.StartTime < b.EndTime &&
                    booking.EndTime > b.StartTime);

                if (slotTakenByAnother)
                {
                    Console.WriteLine(
                        $"Xendit webhook: payment for booking {booking.Id} (invoice {invoiceId}) " +
                        "arrived after its slot was already re-booked by someone else. " +
                        "NOT auto-confirming; needs manual refund/resolution.");
                    return Ok();
                }

                booking.CancelledAt = null;
            }

            if (booking.BookingType != "OpenPlay")
            {
                booking.Status = "Confirmed";
            }
            else if (booking.Status == "Cancelled")
            {
                booking.Status = "Pending";
            }

            booking.PaymentStatus = "Paid";
            booking.PaidAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            try
            {
                await _emailService.SendBookingReceiptAsync(booking, booking.Court);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Receipt email failed for booking {booking.Id} (invoice {invoiceId}): {ex.Message}");
            }

            return Ok();
        }
    }
}
