using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RaceDay.API.Models;

[Table("Enrollments")]
public class Enrollment
{
    [Key]
    public int EnrollmentID { get; set; }

    public int EventID { get; set; }

    public int ParticipantID { get; set; }

    public int CategoryID { get; set; }

    public DateTime EnrollmentDate { get; set; }

    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Enrolled";
}