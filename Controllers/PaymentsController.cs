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
                return Ok(); // Not retryable — malformed body won't parse differently next time.
            }

            if (!root.TryGetProperty("status", out var statusProp) || !root.TryGetProperty("id", out var idProp))
            {
                Console.WriteLine("Xendit webhook: payload missing expected 'status' or 'id' field.");
                return Ok();
            }

            var status = statusProp.GetString();
            var invoiceId = idProp.GetString();

            if (status == "PAID")
            {
                var booking = await _context.Bookings
                    .Include(b => b.Court)
                    .FirstOrDefaultAsync(b => b.XenditInvoiceId == invoiceId);

                if (booking != null && booking.Court != null)
                {
                    // Idempotency: Xendit retries webhooks that don't get a
                    // clean 200 fast enough, or just as routine redundancy.
                    // Without this check, a retry would resend the receipt
                    // email and stomp PaidAt with a new timestamp even though
                    // nothing about the booking actually changed.
                    if (booking.PaymentStatus == "Paid")
                    {
                        return Ok();
                    }

                    // The callback token check above proves this request came
                    // from Xendit, but not that this specific payload's
                    // numbers match what we actually invoiced. Recompute the
                    // expected amount the same way CreateXenditInvoice did and
                    // compare before trusting "PAID" — without this, any
                    // unexpected amount/currency in the payload would still
                    // silently confirm the booking.
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

                    // Small tolerance for rounding, not an exact-decimal match —
                    // CreateXenditInvoice always sends "PHP", so that's the only
                    // currency we ever expect back.
                    var amountMatches = paidAmount.HasValue && Math.Abs(paidAmount.Value - expectedAmount) < 1m;
                    var currencyMatches = string.Equals(currency, "PHP", StringComparison.OrdinalIgnoreCase);

                    if (!amountMatches || !currencyMatches)
                    {
                        Console.WriteLine(
                            $"Xendit webhook amount/currency mismatch for invoice {invoiceId} " +
                            $"(booking {booking.Id}): expected {expectedAmount} PHP, got " +
                            $"{(paidAmount.HasValue ? paidAmount.Value.ToString() : "null")} {currency ?? "null"}. " +
                            "Booking NOT marked paid.");
                        // Still 200 so Xendit doesn't retry indefinitely — this
                        // isn't a transient failure on our end, the payload
                        // itself didn't check out.
                        return Ok();
                    }

                    if (booking.Status == "Cancelled")
                    {
                        // This booking already auto-expired (its 15-minute
                        // online-payment window passed — see
                        // AutoExpireStalePendingBookings) before this "paid"
                        // webhook arrived, e.g. a slow bank OTP flow. Only
                        // resurrect it if nobody else has taken the slot in
                        // the meantime; if someone did, we must not
                        // double-book it — leave it cancelled and log this
                        // for manual follow-up (refund via Xendit's
                        // dashboard, or offer the customer a different slot).
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
                                "NOT auto-confirming — needs manual refund/resolution.");
                            return Ok();
                        }

                        booking.CancelledAt = null;
                    }

                    booking.Status = "Confirmed";
                    booking.PaymentStatus = "Paid";
                    booking.PaidAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    // The payment itself is already saved at this point — a
                    // failure sending the receipt shouldn't turn into a 500.
                    // If it did, Xendit would retry, and thanks to the
                    // idempotency check above the retry would just no-op
                    // without ever getting the email sent. Logging and moving
                    // on means the payment stays correctly recorded even if
                    // email delivery needs separate investigation.
                    try
                    {
                        await _emailService.SendBookingReceiptAsync(booking, booking.Court);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Receipt email failed for booking {booking.Id} (invoice {invoiceId}): {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"Xendit webhook: no booking found for invoice {invoiceId} (status PAID). Ignoring.");
                }
            }

            return Ok();
        }
    }
}