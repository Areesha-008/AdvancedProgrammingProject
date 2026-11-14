namespace MarksApi.Dtos;

public class CourseResponse
{
    public int Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public int CreditHours { get; set; }
}

public class CourseWriteRequest
{
    public string? Code { get; set; }
    public string? Title { get; set; }
    public int? CreditHours { get; set; }
}

public class CourseStatsResponse
{
    public int CourseId { get; set; }
    public string CourseCode { get; set; } = string.Empty;
    public int Enrolled { get; set; }
    public int Graded { get; set; }
    public double? Average { get; set; }
    public int? Highest { get; set; }
    public int? Lowest { get; set; }
    public int Passed { get; set; }
}
