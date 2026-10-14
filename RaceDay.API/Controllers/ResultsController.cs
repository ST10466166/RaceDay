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
public class ResultsController : ControllerBase
{
    private readonly RaceDayDbContext _context;

    public ResultsController(RaceDayDbContext context)
    {
        _context = context;
    }

    // POST: api/Results
    // Only Organisers can record race results.
    [Authorize(Roles = "Organiser")]
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] ResultRequest request)
    {
        var enrollment = await _context.Enrollments
            .FindAsync(request.EnrollmentID);

        if (enrollment == null)
        {
            return BadRequest(new
            {
                message = "EnrollmentID does not exist."
            });
        }

        // Prevent multiple results for the same enrollment.
        bool resultExists = await _context.Results
            .AnyAsync(r => r.EnrollmentID == request.EnrollmentID);

        if (resultExists)
        {
            return Conflict(new
            {
                message = "A result already exists for this enrollment."
            });
        }

        var result = new Result
        {
            EnrollmentID = request.EnrollmentID,
            FinishTime = request.FinishTime,
            FinishingPosition = request.FinishingPosition,
            ResultStatus = request.ResultStatus,
            RecordedAt = DateTime.UtcNow
        };

        _context.Results.Add(result);

        await _context.SaveChangesAsync();

        return Ok(result);
    }

    // GET: api/Results/event/2
    // Only Organisers can view results for an event.
    [Authorize(Roles = "Organiser")]
    [HttpGet("event/{eventId:int}")]
    public async Task<ActionResult<IEnumerable<Result>>> GetForEvent(
        int eventId)
    {
        var enrollmentIds = _context.Enrollments
            .Where(e => e.EventID == eventId)
            .Select(e => e.EnrollmentID);

        var results = await _context.Results
            .AsNoTracking()
            .Where(r => enrollmentIds.Contains(r.EnrollmentID))
            .ToListAsync();

        return Ok(results);
    }

    // GET: api/Results/mine
    // Participants can view results for their own enrollments.
    [Authorize(Roles = "Participant")]
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<Result>>> GetMine()
    {
        if (!int.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier),
                out var participantId))
        {
            return Unauthorized(new
            {
                message = "Unable to identify the logged-in participant."
            });
        }

        var enrollmentIds = _context.Enrollments
            .Where(e => e.ParticipantID == participantId)
            .Select(e => e.EnrollmentID);

        var results = await _context.Results
            .AsNoTracking()
            .Where(r => enrollmentIds.Contains(r.EnrollmentID))
            .ToListAsync();

        return Ok(results);
    }

    // Request body for recording a result.
    public class ResultRequest
    {
        public int EnrollmentID { get; set; }

        public TimeSpan? FinishTime { get; set; }

        public int? FinishingPosition { get; set; }

        public string ResultStatus { get; set; } = "Finished";
    }
}