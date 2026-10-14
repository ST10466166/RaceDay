using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;
using RaceDay.API.Models;

namespace RaceDay.API.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class EnrollmentsController : ControllerBase
{
    private readonly RaceDayDbContext _context;
    public EnrollmentsController(RaceDayDbContext context) => _context = context;

    [Authorize(Roles = "Participant")]
    [HttpPost]
    public async Task<IActionResult> Enroll(EnrollmentRequest request)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var participantId))
            return Unauthorized();

        var eventExists = await _context.Events.AnyAsync(e => e.EventID == request.EventID);
        var category = await _context.Categories.SingleOrDefaultAsync(c => c.CategoryID == request.CategoryID);
        if (!eventExists || category == null || category.EventID != request.EventID)
            return BadRequest(new { message = "The event or category is invalid, or the category belongs to another event." });

        if (await _context.Enrollments.AnyAsync(e => e.EventID == request.EventID && e.ParticipantID == participantId))
            return Conflict(new { message = "You are already enrolled in this event." });

        var enrollment = new Enrollment
        {
            EventID = request.EventID,
            ParticipantID = participantId,
            CategoryID = request.CategoryID,
            EnrollmentDate = DateTime.UtcNow,
            Status = "Enrolled"
        };
        _context.Enrollments.Add(enrollment);
        await _context.SaveChangesAsync();
        return Ok(enrollment);
    }

    [Authorize(Roles = "Organiser")]
    [HttpGet("event/{eventId:int}")]
    public async Task<ActionResult<IEnumerable<Enrollment>>> GetForEvent(int eventId) =>
        await _context.Enrollments.AsNoTracking().Where(e => e.EventID == eventId).ToListAsync();

    [Authorize(Roles = "Participant")]
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<Enrollment>>> GetMine()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) return Unauthorized();
        return await _context.Enrollments.AsNoTracking().Where(e => e.ParticipantID == id).ToListAsync();
    }

    [Authorize(Roles = "Participant")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Cancel(int id)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var participantId)) return Unauthorized();
        var item = await _context.Enrollments.SingleOrDefaultAsync(e => e.EnrollmentID == id && e.ParticipantID == participantId);
        if (item == null) return NotFound();
        item.Status = "Cancelled";
        await _context.SaveChangesAsync();
        return NoContent();
    }

    public class EnrollmentRequest
    {
        public int EventID { get; set; }
        public int CategoryID { get; set; }
    }
}
