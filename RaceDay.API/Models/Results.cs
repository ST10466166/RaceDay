using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace RaceDay.API.Models;

[Table("Results")]
public class Result
{
    [Key]
    public int ResultID { get; set; }

    public int EnrollmentID { get; set; }

    public TimeSpan? FinishTime { get; set; }

    public int? FinishingPosition { get; set; }

    [Required]
    [MaxLength(20)]
    public string ResultStatus { get; set; } = "Finished";

    public DateTime RecordedAt { get; set; }
}