using MarksApi.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarksApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<Student> Students => Set<Student>();

    public DbSet<Course> Courses => Set<Course>();

    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    public DbSet<User> Users => Set<User>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Student>(entity =>
        {
            entity.Property(student => student.RegistrationNo).HasMaxLength(20).IsRequired();
            entity.Property(student => student.FullName).HasMaxLength(100).IsRequired();
            entity.Property(student => student.Email)
                .HasMaxLength(150)
                .IsRequired()
                .HasConversion(email => email.Trim().ToLowerInvariant(), email => email);
            entity.Property(student => student.CreatedAtUtc).HasColumnType("timestamptz").IsRequired();

            entity.HasIndex(student => student.RegistrationNo).IsUnique();
            entity.HasIndex(student => student.Email).IsUnique();

            entity.ToTable(table => table.HasCheckConstraint(
                "ck_students_email_lowercase",
                "email = lower(email)"));
        });

        modelBuilder.Entity<Course>(entity =>
        {
            entity.Property(course => course.Code).HasMaxLength(10).IsRequired();
            entity.Property(course => course.Title).HasMaxLength(150).IsRequired();
            entity.Property(course => course.CreditHours).IsRequired();
            entity.Property(course => course.CreatedAtUtc).HasColumnType("timestamptz").IsRequired();

            entity.HasIndex(course => course.Code).IsUnique();

            entity.ToTable(table => table.HasCheckConstraint(
                "ck_courses_credit_hours",
                "credit_hours BETWEEN 1 AND 4"));
        });

        modelBuilder.Entity<Enrollment>(entity =>
        {
            entity.Property(enrollment => enrollment.EnrolledAtUtc).HasColumnType("timestamptz").IsRequired();

            entity.HasOne(enrollment => enrollment.Student)
                .WithMany(student => student.Enrollments)
                .HasForeignKey(enrollment => enrollment.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(enrollment => enrollment.Course)
                .WithMany(course => course.Enrollments)
                .HasForeignKey(enrollment => enrollment.CourseId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasIndex(enrollment => new { enrollment.StudentId, enrollment.CourseId }).IsUnique();
            entity.HasIndex(enrollment => enrollment.CourseId);

            entity.ToTable(table => table.HasCheckConstraint(
                "ck_enrollments_mark",
                "mark IS NULL OR (mark >= 0 AND mark <= 100)"));
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.Property(user => user.Username).HasMaxLength(50).IsRequired();
            entity.Property(user => user.PasswordHash).IsRequired();
            entity.Property(user => user.Role)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();

            entity.HasIndex(user => user.Username).IsUnique();
            entity.HasIndex(user => user.StudentId).IsUnique();

            entity.HasOne(user => user.Student)
                .WithOne(student => student.User)
                .HasForeignKey<User>(user => user.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.ToTable(table =>
            {
                table.HasCheckConstraint(
                    "ck_users_role",
                    "role IN ('Teacher', 'Student')");
                table.HasCheckConstraint(
                    "ck_users_student_link",
                    "(role = 'Teacher' AND student_id IS NULL) OR (role = 'Student' AND student_id IS NOT NULL)");
            });
        });
    }
}
