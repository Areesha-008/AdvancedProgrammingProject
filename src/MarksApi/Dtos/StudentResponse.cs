namespace MarksApi.Dtos;

public class StudentResponse
{
    public int Id { get; set; }
    public string RegistrationNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}

public class StudentWriteRequest
{
    public string? RegistrationNo { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
}

public class TranscriptResponse
{
    public int StudentId { get; set; }
    public string RegistrationNo { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public IReadOnlyList<TranscriptCourse> Courses { get; set; } = [];
    public double? WeightedAverage { get; set; }
}

public class TranscriptCourse
{
    public string Code { get; set; } = string.Empty;
    public int CreditHours { get; set; }
    public int? Mark { get; set; }
    public bool Passed { get; set; }
}