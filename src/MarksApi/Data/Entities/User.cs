namespace MarksApi.Data.Entities;

public class User
{
    public int Id { get; set; }

    public string Username { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public UserRole Role { get; set; }

    public int? StudentId { get; set; }

    public Student? Student { get; set; }
}
