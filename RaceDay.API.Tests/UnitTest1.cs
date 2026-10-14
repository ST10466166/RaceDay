using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using RaceDay.API.Controllers;
using RaceDay.API.Data;
using RaceDay.API.DTOs;
using RaceDay.API.Models;
using RaceDay.API.Services;
using Xunit;

namespace RaceDay.API.Tests;

public class UnitTest1
{
    private static RaceDayDbContext CreateDatabase()
    {
        var options = new DbContextOptionsBuilder<RaceDayDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new RaceDayDbContext(options);
    }

    private static TokenService CreateTokenService()
    {
        var settings = new Dictionary<string, string?>
        {
            ["Jwt:Key"] =
                "RaceDay-Test-Key-1234567890-ABCDEF-1234567890",
            ["Jwt:Issuer"] = "RaceDay.API",
            ["Jwt:Audience"] = "RaceDay.Client"
        };

        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new TokenService(configuration);
    }

    // 1. BASIC TEST

    [Fact]
    public void Addition_TwoNumbers_ReturnsCorrectResult()
    {
        int number1 = 10;
        int number2 = 5;

        int result = number1 + number2;

        Assert.Equal(15, result);
    }

    // 2. RESULTS DEFAULT VALUE

    [Fact]
    public void ResultStatus_DefaultValue_IsFinished()
    {
        var request = new ResultsController.ResultRequest();

        Assert.Equal("Finished", request.ResultStatus);
    }

    // 3. REGISTRATION

    [Fact]
    public async Task Registration_ValidParticipant_CreatesAccount()
    {
        using var db = CreateDatabase();

        var controller = new AuthController(
            db,
            CreateTokenService());

        var request = new RegisterRequest
        {
            FirstName = "Test",
            LastName = "Participant",
            Email = "participant@test.com",
            Password = "TestPassword@123",
            Role = "Participant"
        };

        var result = await controller.Register(request);

        Assert.IsType<CreatedResult>(result);
        Assert.Equal(1, await db.Users.CountAsync());

        var user = await db.Users.SingleAsync();

        Assert.Equal("participant@test.com", user.Email);
        Assert.Equal("Participant", user.Role);
        Assert.NotEqual("TestPassword@123", user.PasswordHash);
    }

    [Fact]
    public async Task Registration_InvalidRole_ReturnsBadRequest()
    {
        using var db = CreateDatabase();

        var controller = new AuthController(
            db,
            CreateTokenService());

        var request = new RegisterRequest
        {
            FirstName = "Test",
            LastName = "User",
            Email = "invalid@test.com",
            Password = "TestPassword@123",
            Role = "Administrator"
        };

        var result = await controller.Register(request);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(await db.Users.ToListAsync());
    }

    // 4. LOGIN

    [Fact]
    public async Task Login_ValidCredentials_ReturnsToken()
    {
        using var db = CreateDatabase();

        var controller = new AuthController(
            db,
            CreateTokenService());

        await controller.Register(new RegisterRequest
        {
            FirstName = "Test",
            LastName = "User",
            Email = "login@test.com",
            Password = "TestPassword@123",
            Role = "Participant"
        });

        var result = await controller.Login(new LoginRequest
        {
            Email = "login@test.com",
            Password = "TestPassword@123"
        });

        var response = Assert.IsType<OkObjectResult>(result);

        Assert.NotNull(response.Value);

        var tokenProperty = response.Value.GetType()
            .GetProperty("token");

        Assert.NotNull(tokenProperty);

        var token = tokenProperty!.GetValue(response.Value);

        Assert.NotNull(token);
        Assert.NotEmpty(token.ToString()!);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorized()
    {
        using var db = CreateDatabase();

        var controller = new AuthController(
            db,
            CreateTokenService());

        await controller.Register(new RegisterRequest
        {
            FirstName = "Test",
            LastName = "User",
            Email = "wrongpassword@test.com",
            Password = "CorrectPassword@123",
            Role = "Participant"
        });

        var result = await controller.Login(new LoginRequest
        {
            Email = "wrongpassword@test.com",
            Password = "IncorrectPassword@123"
        });

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    // 5. EVENT MANAGEMENT

    [Fact]
    public async Task Events_GetEvents_ReturnsEventList()
    {
        using var db = CreateDatabase();

        db.Events.Add(new RaceDay.API.Models.Event
        {
            EventName = "Test Race",
            Description = "Unit test event",
            EventDate = DateTime.Today.AddDays(7),
            Location = "Pretoria",
            DistanceKm = 5,
            EventType = "Running",
            Status = "Scheduled",
            OrganiserID = 1,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var controller = new EventsController(db);

        var result = await controller.GetEvents();

        var actionResult =
            Assert.IsType<ActionResult<IEnumerable<RaceDay.API.Models.Event>>>(result);

        var events = Assert.IsAssignableFrom<
            IEnumerable<RaceDay.API.Models.Event>>(actionResult.Value);

        Assert.Single(events);
        Assert.Equal("Test Race", events.First().EventName);
    }

    [Fact]
    public async Task Events_GetMissingEvent_ReturnsNotFound()
    {
        using var db = CreateDatabase();

        var controller = new EventsController(db);

        var result = await controller.GetEvent(999);

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    // 6. ENROLMENTS

    [Fact]
    public async Task Enrolment_ParticipantCanEnrolInValidEvent()
    {
        using var db = CreateDatabase();

        db.Events.Add(new RaceDay.API.Models.Event
        {
            EventName = "Test Race",
            Description = "Unit test event",
            EventDate = DateTime.Today.AddDays(7),
            Location = "Pretoria",
            DistanceKm = 5,
            EventType = "Running",
            Status = "Scheduled",
            OrganiserID = 1,
            CreatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        var eventId = (await db.Events.SingleAsync()).EventID;

        db.Categories.Add(new Category
        {
            EventID = eventId,
            CategoryName = "5 KM",
            CategoryType = "Running",
            Description = "Five kilometre race"
        });

        await db.SaveChangesAsync();

        var categoryId = (await db.Categories.SingleAsync()).CategoryID;

        var controller = new EnrollmentsController(db);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };

        controller.HttpContext.User = new ClaimsPrincipal(
            new ClaimsIdentity(
                new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, "6"),
                    new Claim(ClaimTypes.Role, "Participant")
                },
                "TestAuthentication"));

        var result = await controller.Enroll(
            new EnrollmentsController.EnrollmentRequest
            {
                EventID = eventId,
                CategoryID = categoryId
            });

        Assert.IsType<OkObjectResult>(result);
        Assert.Single(await db.Enrollments.ToListAsync());

        var enrollment = await db.Enrollments.SingleAsync();

        Assert.Equal(6, enrollment.ParticipantID);
        Assert.Equal(eventId, enrollment.EventID);
    }

    // 7. ROLE RESTRICTIONS

    [Fact]
    public void EventCreation_RequiresOrganiserRole()
    {
        var method = typeof(EventsController).GetMethod(
            nameof(EventsController.CreateEvent));

        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Organiser", authorize!.Roles);
    }

    [Fact]
    public void EnrolmentCreation_RequiresParticipantRole()
    {
        var method = typeof(EnrollmentsController).GetMethod(
            nameof(EnrollmentsController.Enroll));

        Assert.NotNull(method);

        var authorize = method!.GetCustomAttribute<AuthorizeAttribute>();

        Assert.NotNull(authorize);
        Assert.Equal("Participant", authorize!.Roles);
    }
}