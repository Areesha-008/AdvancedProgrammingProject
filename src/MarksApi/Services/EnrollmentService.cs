using MarksApi.Data;
using MarksApi.Data.Entities;
using MarksApi.Dtos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarksApi.Services;

public interface IEnrollmentService
{
    Task<PagedResult<EnrollmentResponse>?> ListAsync(int courseId, int page, int pageSize, string? search, string? sort, CancellationToken cancellationToken);
    Task<EnrollmentResponse> EnrollAsync(int courseId, int studentId, CancellationToken cancellationToken);
    Task<EnrollmentResponse?> UpdateMarkAsync(int id, int mark, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}

public class EnrollmentService : IEnrollmentService
{
    private readonly AppDbContext _db;
    private readonly ILogger<EnrollmentService> _logger;

    public EnrollmentService(AppDbContext db, ILogger<EnrollmentService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PagedResult<EnrollmentResponse>?> ListAsync(int courseId, int page, int pageSize, string? search, string? sort, CancellationToken cancellationToken)
    {
        if (!await _db.Courses.AnyAsync(course => course.Id == courseId, cancellationToken))
        {
            return null;
        }

        (page, pageSize) = Normalize(page, pageSize);
        var query = _db.Enrollments.AsNoTracking().Where(enrollment => enrollment.CourseId == courseId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(enrollment =>
                enrollment.Student.FullName.ToLower().Contains(term) ||
                enrollment.Student.RegistrationNo.ToLower().Contains(term));
        }

        query = sort switch
        {
            "registrationNo" => query.OrderBy(enrollment => enrollment.Student.RegistrationNo),
            "mark" => query.OrderBy(enrollment => enrollment.Mark),
            "enrolledAtUtc" => query.OrderBy(enrollment => enrollment.EnrolledAtUtc),
            _ => query.OrderBy(enrollment => enrollment.Student.FullName)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(enrollment => new EnrollmentResponse
            {
                Id = enrollment.Id,
                StudentId = enrollment.StudentId,
                CourseId = enrollment.CourseId,
                Mark = enrollment.Mark
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<EnrollmentResponse> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }

    public async Task<EnrollmentResponse> EnrollAsync(int courseId, int studentId, CancellationToken cancellationToken)
    {
        if (!await _db.Courses.AnyAsync(course => course.Id == courseId, cancellationToken))
        {
            throw new ApiNotFoundException("No course with this id exists.");
        }

        if (!await _db.Students.AnyAsync(student => student.Id == studentId, cancellationToken))
        {
            throw new ApiNotFoundException("No student with this id exists.");
        }

        var enrollment = new Enrollment
        {
            CourseId = courseId,
            StudentId = studentId,
            EnrolledAtUtc = DateTime.UtcNow
        };
        _db.Enrollments.Add(enrollment);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsPostgres(exception, PostgresErrorCodes.UniqueViolation))
        {
            throw new ApiConflictException("Duplicate enrollment", "This student is already enrolled in this course.");
        }
        catch (DbUpdateException exception) when (IsPostgres(exception, PostgresErrorCodes.ForeignKeyViolation))
        {
            throw new ApiNotFoundException("The student or the course no longer exists.");
        }

        _logger.LogInformation("Enrolled student {StudentId} in course {CourseId}", studentId, courseId);
        return ToResponse(enrollment);
    }

    public async Task<EnrollmentResponse?> UpdateMarkAsync(int id, int mark, CancellationToken cancellationToken)
    {
        var enrollment = await _db.Enrollments.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (enrollment is null)
        {
            return null;
        }

        enrollment.Mark = mark;
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Recorded mark {Mark} for enrollment {EnrollmentId}", mark, id);
        return ToResponse(enrollment);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var enrollment = await _db.Enrollments.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (enrollment is null)
        {
            return false;
        }

        _db.Enrollments.Remove(enrollment);
        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Deleted enrollment {EnrollmentId}", id);
        return true;
    }

    private static bool IsPostgres(DbUpdateException exception, string code)
    {
        return exception.InnerException is PostgresException postgres && postgres.SqlState == code;
    }

    private static (int Page, int PageSize) Normalize(int page, int pageSize)
    {
        if (page < 1)
        {
            page = 1;
        }
        if (pageSize < 1)
        {
            pageSize = 10;
        }
        if (pageSize > 50)
        {
            pageSize = 50;
        }

        return (page, pageSize);
    }

    private static EnrollmentResponse ToResponse(Enrollment enrollment)
    {
        return new EnrollmentResponse
        {
            Id = enrollment.Id,
            StudentId = enrollment.StudentId,
            CourseId = enrollment.CourseId,
            Mark = enrollment.Mark
        };
    }
}
