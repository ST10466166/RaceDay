using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;
using RaceDay.API.Models;

namespace RaceDay.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class EventsController : ControllerBase
{
    private readonly RaceDayDbContext _context;
    public EventsController(RaceDayDbContext context) => _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Event>>> GetEvents() =>
        await _context.Events.AsNoTracking().ToListAsync();

    [HttpGet("{id:int}")]
    public async Task<ActionResult<Event>> GetEvent(int id)
    {
        var item = await _context.Events.AsNoTracking().FirstOrDefaultAsync(e => e.EventID == id);
        return item == null ? NotFound(new { message = "Event not found." }) : Ok(item);
    }

    [Authorize(Roles = "Organiser")]
    [HttpPost]
    public async Task<ActionResult<Event>> CreateEvent(Event item)
    {
        item.CreatedAt = DateTime.UtcNow;
        _context.Events.Add(item);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetEvent), new { id = item.EventID }, item);
    }

    [Authorize(Roles = "Organiser")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateEvent(int id, Event request)
    {
        if (id != request.EventID) return BadRequest(new { message = "Route ID and EventID must match." });
        var item = await _context.Events.FindAsync(id);
        if (item == null) return NotFound(new { message = "Event not found." });

        item.EventName = request.EventName;
        item.Description = request.Description;
        item.EventDate = request.EventDate;
        item.Location = request.Location;
        item.DistanceKm = request.DistanceKm;
        item.EventType = request.EventType;
        item.Status = request.Status;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = "Organiser")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        var item = await _context.Events.FindAsync(id);
        if (item == null) return NotFound(new { message = "Event not found." });
        _context.Events.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
