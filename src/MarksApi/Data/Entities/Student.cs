namespace MarksApi.Data.Entities;

public class Student
{
    public int Id { get; set; }

    public string RegistrationNo { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public DateTime CreatedAtUtc { get; set; }

    public ICollection<Enrollment> Enrollments { get; set; } = new List<Enrollment>();

    public User? User { get; set; }
}
