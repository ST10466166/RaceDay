using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;

namespace RaceDay.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DatabaseController : ControllerBase
{
    private readonly RaceDayDbContext _context;

    public DatabaseController(RaceDayDbContext context)
    {
        _context = context;
    }

    [HttpGet("test")]
    public async Task<IActionResult> TestConnection()
    {
        try
        {
            bool connected = await _context.Database.CanConnectAsync();

            if (connected)
            {
                return Ok(new
                {
                    message = "Database connection successful!",
                    database = "RaceDayDB"
                });
            }

            return StatusCode(500, new
            {
                message = "Could not connect to RaceDayDB."
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                message = "Database connection failed.",
                error = ex.Message
            });
        }
    }
}