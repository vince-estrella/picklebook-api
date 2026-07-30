using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballApi.Models;
using System.Security.Claims;

namespace PickleballApi.Controllers
{
    // Kept as its own controller file rather than folded into your existing
    // CourtsController, since I don't have that file's current contents and
    // don't want to guess-overwrite it. This is just another set of actions
    // under the same "api/courts" route prefix — ASP.NET Core is fine with
    // multiple controller classes sharing a route prefix as long as the
    // individual method+route combinations don't collide with anything
    // CourtsController already defines. Feel free to move ReportCourt into
    // CourtsController later and delete this file if you'd rather keep
    // everything in one place.
    [ApiController]
    [Route("api/courts")]
    public class CourtReportsController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CourtReportsController(AppDbContext context)
        {
            _context = context;
        }

        public class ReportCourtDto
        {
            public string Reason { get; set; } = string.Empty;
            public string? Details { get; set; }
        }

        // POST: api/courts/5/report
        // No [Authorize] — ReportListingPage.jsx doesn't require login before
        // submitting, so guests can report too. If the request does carry a
        // valid player token anyway, we still capture which player it was.
        [HttpPost("{id}/report")]
        public async Task<ActionResult> ReportCourt(int id, [FromBody] ReportCourtDto dto)
        {
            if (string.IsNullOrWhiteSpace(dto.Reason))
            {
                return BadRequest("A reason is required.");
            }

            var courtExists = await _context.Courts.AnyAsync(c => c.Id == id);
            if (!courtExists)
            {
                return NotFound("Court does not exist.");
            }

            int? userId = null;
            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            var roleClaim = User.FindFirstValue(ClaimTypes.Role);
            if (userIdClaim != null && roleClaim == "Player")
            {
                userId = int.Parse(userIdClaim);
            }

            var report = new Report
            {
                CourtId = id,
                UserId = userId,
                Reason = dto.Reason,
                Details = dto.Details,
            };

            _context.Reports.Add(report);
            await _context.SaveChangesAsync();

            return Ok(new { report.Id });
        }
    }
}