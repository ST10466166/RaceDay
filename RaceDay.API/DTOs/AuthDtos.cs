using System.ComponentModel.DataAnnotations;

namespace RaceDay.API.DTOs;

public class RegisterRequest
{
    [Required, MaxLength(50)]
    public string FirstName { get; set; } = "Musa";

    [Required, MaxLength(50)]
    public string LastName { get; set; } = "Test";

    [Required, EmailAddress, MaxLength(100)]
    public string Email { get; set; } = "musa.test@example.com";

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
    public string Email { get; set; } = "musa.test@example.com";

    [Required]
    public string Password { get; set; } = string.Empty;
}