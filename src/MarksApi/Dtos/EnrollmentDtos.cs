namespace MarksApi.Dtos;

public class EnrollmentResponse
{
    public int Id { get; set; }
    public int StudentId { get; set; }
    public int CourseId { get; set; }
    public int? Mark { get; set; }
}

public class EnrollRequest
{
    public int? StudentId { get; set; }
}

public class MarkRequest
{
    public int? Mark { get; set; }
}
