using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;
using RaceDay.API.Models;

namespace RaceDay.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CategoriesController : ControllerBase
{
    private readonly RaceDayDbContext _context;
    public CategoriesController(RaceDayDbContext context) => _context = context;

    [HttpGet]
    public async Task<ActionResult<IEnumerable<Category>>> GetAll() =>
        await _context.Categories.AsNoTracking().ToListAsync();

    [HttpGet("event/{eventId:int}")]
    public async Task<ActionResult<IEnumerable<Category>>> GetForEvent(int eventId) =>
        await _context.Categories.AsNoTracking().Where(c => c.EventID == eventId).ToListAsync();

    [Authorize(Roles = "Organiser")]
    [HttpPost]
    public async Task<ActionResult<Category>> Create(Category category)
    {
        if (!await _context.Events.AnyAsync(e => e.EventID == category.EventID))
            return BadRequest(new { message = "EventID does not exist." });
        _context.Categories.Add(category);
        await _context.SaveChangesAsync();
        return CreatedAtAction(nameof(GetForEvent), new { eventId = category.EventID }, category);
    }

    [Authorize(Roles = "Organiser")]
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, Category request)
    {
        var item = await _context.Categories.FindAsync(id);
        if (item == null) return NotFound();
        item.CategoryName = request.CategoryName;
        item.CategoryType = request.CategoryType;
        item.Description = request.Description;
        await _context.SaveChangesAsync();
        return NoContent();
    }

    [Authorize(Roles = "Organiser")]
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        var item = await _context.Categories.FindAsync(id);
        if (item == null) return NotFound();
        _context.Categories.Remove(item);
        await _context.SaveChangesAsync();
        return NoContent();
    }
}
