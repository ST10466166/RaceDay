$ErrorActionPreference = "Stop"

# Run this script from the RaceDay solution folder or the RaceDay.API project folder.
$project = $null

if (Test-Path (Join-Path (Get-Location) "RaceDay.API.csproj")) {
    $project = (Get-Location).Path
}
elseif (Test-Path (Join-Path (Get-Location) "RaceDay.API\RaceDay.API.csproj")) {
    $project = (Join-Path (Get-Location) "RaceDay.API")
}
else {
    $found = Get-ChildItem -Path (Get-Location) -Filter "RaceDay.API.csproj" -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $project = $found.Directory.FullName }
}

if (-not $project) {
    Write-Host "Could not find RaceDay.API.csproj. Put this script in the RaceDay solution folder and run it again." -ForegroundColor Red
    exit 1
}

Write-Host "Project found: $project" -ForegroundColor Cyan
$backup = Join-Path $project ("backup_before_autosetup_" + (Get-Date -Format "yyyyMMdd_HHmmss"))
New-Item -ItemType Directory -Path $backup -Force | Out-Null

# Back up files this script may update.
$backupFiles = @("Program.cs", "appsettings.json", "RaceDay.API.csproj")
foreach ($name in $backupFiles) {
    $p = Join-Path $project $name
    if (Test-Path $p) { Copy-Item $p (Join-Path $backup $name) -Force }
}
$eventsPath = Join-Path $project "Controllers\EventsController.cs"
if (Test-Path $eventsPath) { Copy-Item $eventsPath (Join-Path $backup "EventsController.cs") -Force }

# Create required folders.
@("Controllers", "DTOs", "Services", ".github\workflows") | ForEach-Object {
    New-Item -ItemType Directory -Path (Join-Path $project $_) -Force | Out-Null
}

# Add JWT bearer package if it is not already referenced.
$csprojPath = Join-Path $project "RaceDay.API.csproj"
$csproj = Get-Content $csprojPath -Raw
if ($csproj -notmatch 'Microsoft.AspNetCore.Authentication.JwtBearer') {
    $replacement = '    <PackageReference Include="Microsoft.AspNetCore.Authentication.JwtBearer" Version="10.0.0" />' + "`r`n" + '$1'
    $csproj = [regex]::Replace($csproj, '(</ItemGroup>)', $replacement, 1)
    Set-Content -Path $csprojPath -Value $csproj -Encoding UTF8
}

function Write-ProjectFile([string]$relativePath, [string]$content) {
    $fullPath = Join-Path $project $relativePath
    $parent = Split-Path $fullPath -Parent
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    Set-Content -Path $fullPath -Value $content -Encoding UTF8
    Write-Host "Created: $relativePath"
}

Write-ProjectFile "DTOs\AuthDtos.cs" @'
using System.ComponentModel.DataAnnotations;

namespace RaceDay.API.DTOs;

public class RegisterRequest
{
    [Required, MaxLength(50)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(50)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(8), MaxLength(100)]
    public string Password { get; set; } = string.Empty;

    [Required]
    public string Role { get; set; } = "Participant";

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }
}

public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}
'@

Write-ProjectFile "Services\TokenService.cs" @'
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using RaceDay.API.Models;

namespace RaceDay.API.Services;

public class TokenService
{
    private readonly IConfiguration _configuration;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public string CreateToken(User user)
    {
        var keyText = _configuration["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is missing from configuration.");
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(keyText));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
            new Claim(ClaimTypes.Name, user.Email),
            new Claim(ClaimTypes.Role, user.Role)
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.UtcNow.AddHours(2),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
'@

Write-ProjectFile "Controllers\AuthController.cs" @'
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;
using RaceDay.API.Services;

namespace RaceDay.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly RaceDayDbContext _context;
    private readonly PasswordHasher<User> _hasher = new();
    private readonly TokenService _tokens;

    public AuthController(RaceDayDbContext context, TokenService tokens)
    {
        _context = context;
        _tokens = tokens;
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var role = request.Role.Trim();
        if (role != "Organiser" && role != "Participant")
            return BadRequest(new { message = "Role must be Organiser or Participant." });

        var email = request.Email.Trim().ToLowerInvariant();
        if (await _context.Users.AnyAsync(u => u.Email == email))
            return Conflict(new { message = "An account with this email already exists." });

        var user = new User
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Email = email,
            PhoneNumber = request.PhoneNumber,
            Role = role,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = _hasher.HashPassword(user, request.Password);

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        return Created("/api/auth/login", new
        {
            user.UserID, user.FirstName, user.LastName, user.Email, user.Role
        });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _context.Users.SingleOrDefaultAsync(u => u.Email == email);
        if (user == null)
            return Unauthorized(new { message = "Invalid email or password." });

        var verification = _hasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
            return Unauthorized(new { message = "Invalid email or password." });

        return Ok(new
        {
            token = _tokens.CreateToken(user),
            user = new { user.UserID, user.FirstName, user.LastName, user.Email, user.Role }
        });
    }
}
'@

Write-ProjectFile "Controllers\UsersController.cs" @'
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
'@

Write-ProjectFile "Controllers\EventsController.cs" @'
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
'@

Write-ProjectFile "Controllers\CategoriesController.cs" @'
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
'@

Write-ProjectFile "Controllers\EnrollmentsController.cs" @'
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
'@

Write-ProjectFile "Controllers\ResultsController.cs" @'
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
    public ResultsController(RaceDayDbContext context) => _context = context;

    [Authorize(Roles = "Organiser")]
    [HttpPost]
    public async Task<IActionResult> Create(ResultRequest request)
    {
        var enrollment = await _context.Enrollments.FindAsync(request.EnrollmentID);
        if (enrollment == null) return BadRequest(new { message = "EnrollmentID does not exist." });
        if (await _context.Results.AnyAsync(r => r.EnrollmentID == request.EnrollmentID))
            return Conflict(new { message = "A result already exists for this enrollment." });

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

    [Authorize(Roles = "Organiser")]
    [HttpGet("event/{eventId:int}")]
    public async Task<ActionResult<IEnumerable<Result>>> GetForEvent(int eventId)
    {
        var enrollmentIds = _context.Enrollments.Where(e => e.EventID == eventId).Select(e => e.EnrollmentID);
        return await _context.Results.AsNoTracking().Where(r => enrollmentIds.Contains(r.EnrollmentID)).ToListAsync();
    }

    [Authorize(Roles = "Participant")]
    [HttpGet("mine")]
    public async Task<ActionResult<IEnumerable<Result>>> GetMine()
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var participantId)) return Unauthorized();
        var enrollmentIds = _context.Enrollments.Where(e => e.ParticipantID == participantId).Select(e => e.EnrollmentID);
        return await _context.Results.AsNoTracking().Where(r => enrollmentIds.Contains(r.EnrollmentID)).ToListAsync();
    }

    public class ResultRequest
    {
        public int EnrollmentID { get; set; }
        public TimeSpan? FinishTime { get; set; }
        public int? FinishingPosition { get; set; }
        public string ResultStatus { get; set; } = "Finished";
    }
}
'@

Write-ProjectFile "appsettings.json" @'
{
  "ConnectionStrings": {
    "RaceDayConnection": "Server=Musa\\SQLEXPRESS;Database=RaceDayDB;Trusted_Connection=True;TrustServerCertificate=True;"
  },
  "Jwt": {
    "Key": "RaceDay-Development-Key-Change-This-1234567890-ABCDEF",
    "Issuer": "RaceDay.API",
    "Audience": "RaceDay.Client"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  },
  "AllowedHosts": "*"
}
'@

Write-ProjectFile "Program.cs" @'
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using RaceDay.API.Data;
using RaceDay.API.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<RaceDayDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("RaceDayConnection")));

builder.Services.AddScoped<TokenService>();

var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is missing.");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => Results.Redirect("/swagger"));
app.Run();
'@

Write-ProjectFile ".github\workflows\dotnet.yml" @'
name: RaceDay API build

on:
  push:
  pull_request:

jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Install .NET 10
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: 10.0.x
      - name: Restore
        run: dotnet restore RaceDay.API/RaceDay.API.csproj
      - name: Build
        run: dotnet build RaceDay.API/RaceDay.API.csproj --no-restore
'@

Write-Host ""
Write-Host "Files generated. A backup of the previous configuration is at:" -ForegroundColor Green
Write-Host $backup -ForegroundColor Yellow
Write-Host ""
Write-Host "NEXT: In Visual Studio, build the project. If the build succeeds, run it and test POST /api/Auth/register and POST /api/Auth/login in Swagger." -ForegroundColor Cyan
Write-Host "Do not run migrations; this project maps to your existing RaceDayDB tables."
