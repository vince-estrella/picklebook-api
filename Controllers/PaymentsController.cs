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
            var root = JsonDocument.Parse(rawBody).RootElement;

            var status = root.GetProperty("status").GetString();
            var invoiceId = root.GetProperty("id").GetString();

            if (status == "PAID")
            {
                var booking = await _context.Bookings
                    .Include(b => b.Court)
                    .FirstOrDefaultAsync(b => b.XenditInvoiceId == invoiceId);
                if (booking != null)
                {
                    booking.Status = "Confirmed";
                    booking.PaymentStatus = "Paid";
                    booking.PaidAt = DateTime.UtcNow;
                    await _context.SaveChangesAsync();

                    if (booking.Court != null)
                    {
                        await _emailService.SendBookingReceiptAsync(booking, booking.Court);
                    }
                }
            }

            return Ok();
        }
    }
}