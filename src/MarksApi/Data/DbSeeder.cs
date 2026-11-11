using MarksApi.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace MarksApi.Data;

public static class DbSeeder
{
    public static async Task SeedAsync(AppDbContext db, IConfiguration configuration, CancellationToken cancellationToken)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        var password = configuration["Seed:DemoPassword"];
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new InvalidOperationException("Seed:DemoPassword is not configured.");
        }

        var teacherUsername = configuration["Seed:TeacherUsername"];
        var firstStudentUsername = configuration["Seed:StudentUsernames:0"];
        var secondStudentUsername = configuration["Seed:StudentUsernames:1"];
        if (string.IsNullOrWhiteSpace(teacherUsername)
            || string.IsNullOrWhiteSpace(firstStudentUsername)
            || string.IsNullOrWhiteSpace(secondStudentUsername))
        {
            throw new InvalidOperationException("Seed usernames are not configured.");
        }

        var now = DateTime.UtcNow;
        var hasher = new PasswordHasher<User>();

        var ali = new Student
        {
            RegistrationNo = "CS-2022-001",
            FullName = "Ali Raza",
            Email = "ali.raza@example.com",
            CreatedAtUtc = now
        };
        var sara = new Student
        {
            RegistrationNo = "CS-2022-002",
            FullName = "Sara Khan",
            Email = "sara.khan@example.com",
            CreatedAtUtc = now
        };
        var cs401 = new Course
        {
            Code = "CS401",
            Title = "Advanced Programming",
            CreditHours = 3,
            CreatedAtUtc = now
        };
        var ma201 = new Course
        {
            Code = "MA201",
            Title = "Calculus",
            CreditHours = 2,
            CreatedAtUtc = now
        };
        var cs301 = new Course
        {
            Code = "CS301",
            Title = "Databases",
            CreditHours = 4,
            CreatedAtUtc = now
        };

        db.Students.AddRange(ali, sara);
        db.Courses.AddRange(cs401, ma201, cs301);
        await db.SaveChangesAsync(cancellationToken);

        var teacher = CreateUser(hasher, teacherUsername, password, UserRole.Teacher, studentId: null);
        var firstStudent = CreateUser(hasher, firstStudentUsername, password, UserRole.Student, ali.Id);
        var secondStudent = CreateUser(hasher, secondStudentUsername, password, UserRole.Student, sara.Id);
        db.Users.AddRange(teacher, firstStudent, secondStudent);

        db.Enrollments.AddRange(
            Enrollment(ali.Id, cs401.Id, 80, now),
            Enrollment(ali.Id, ma201.Id, 60, now),
            Enrollment(ali.Id, cs301.Id, null, now),
            Enrollment(sara.Id, cs401.Id, 70, now),
            Enrollment(sara.Id, ma201.Id, null, now),
            Enrollment(sara.Id, cs301.Id, 45, now));

        await db.SaveChangesAsync(cancellationToken);
    }

    private static User CreateUser(PasswordHasher<User> hasher, string username, string password, UserRole role, int? studentId)
    {
        var user = new User
        {
            Username = username,
            Role = role,
            StudentId = studentId
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        return user;
    }

    private static Enrollment Enrollment(int studentId, int courseId, int? mark, DateTime enrolledAtUtc)
    {
        return new Enrollment
        {
            StudentId = studentId,
            CourseId = courseId,
            Mark = mark,
            EnrolledAtUtc = enrolledAtUtc
        };
    }
}
