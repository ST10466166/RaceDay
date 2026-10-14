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
public class UsersController : ControllerBase
{
    private readonly RaceDayDbContext _context;

    public UsersController(RaceDayDbContext context) => _context = context;

    private bool TryGetUserId(out int id) =>
        int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out id);

    [HttpGet("me")]
    public async Task<IActionResult> GetMyProfile()
    {
        if (!TryGetUserId(out var id)) return Unauthorized();
        var user = await _context.Users.AsNoTracking()
            .Where(u => u.UserID == id)
            .Select(u => new { u.UserID, u.FirstName, u.LastName, u.Email, u.PhoneNumber, u.Role, u.CreatedAt })
            .FirstOrDefaultAsync();
        return user == null ? NotFound() : Ok(user);
    }

    [HttpPut("me")]
    public async Task<IActionResult> UpdateMyProfile(UserProfileUpdate request)
    {
        if (!TryGetUserId(out var id)) return Unauthorized();
        var user = await _context.Users.FindAsync(id);
        if (user == null) return NotFound();

        user.FirstName = request.FirstName;
        user.LastName = request.LastName;
        user.PhoneNumber = request.PhoneNumber;

        var profile = await _context.UserProfiles.SingleOrDefaultAsync(p => p.UserID == id);
        if (profile == null)
        {
            profile = new UserProfile { UserID = id };
            _context.UserProfiles.Add(profile);
        }
        profile.DateOfBirth = request.DateOfBirth;
        profile.Gender = request.Gender;
        profile.EmergencyContactName = request.EmergencyContactName;
        profile.EmergencyContactPhone = request.EmergencyContactPhone;
        profile.Address = request.Address;

        await _context.SaveChangesAsync();
        return NoContent();
    }

    public class UserProfileUpdate
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string? PhoneNumber { get; set; }
        public DateTime? DateOfBirth { get; set; }
        public string? Gender { get; set; }
        public string? EmergencyContactName { get; set; }
        public string? EmergencyContactPhone { get; set; }
        public string? Address { get; set; }
    }
}
