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