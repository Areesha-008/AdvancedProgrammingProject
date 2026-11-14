using MarksApi.Data;
using MarksApi.Data.Entities;
using MarksApi.Dtos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarksApi.Services;

public class StudentService : IStudentService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<StudentService> _logger;

    public StudentService(AppDbContext db, IConfiguration configuration, ILogger<StudentService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PagedResult<StudentResponse>> ListAsync(int page, int pageSize, string? search, string? sort, CancellationToken cancellationToken)
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

        var query = _db.Students.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(student =>
                student.FullName.ToLower().Contains(term) ||
                student.RegistrationNo.ToLower().Contains(term) ||
                student.Email.ToLower().Contains(term));
        }

        query = sort switch
        {
            "registrationNo" => query.OrderBy(student => student.RegistrationNo),
            "email" => query.OrderBy(student => student.Email),
            "createdAtUtc" => query.OrderBy(student => student.CreatedAtUtc),
            _ => query.OrderBy(student => student.FullName)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(student => new StudentResponse
            {
                Id = student.Id,
                RegistrationNo = student.RegistrationNo,
                FullName = student.FullName,
                Email = student.Email
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<StudentResponse>
        {
            Items = items,
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }

    public async Task<StudentResponse?> GetAsync(int id, CancellationToken cancellationToken)
    {
        return await _db.Students.AsNoTracking()
            .Where(student => student.Id == id)
            .Select(student => new StudentResponse
            {
                Id = student.Id,
                RegistrationNo = student.RegistrationNo,
                FullName = student.FullName,
                Email = student.Email
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<StudentResponse> CreateAsync(StudentWriteRequest request, CancellationToken cancellationToken)
    {
        var student = new Student
        {
            RegistrationNo = request.RegistrationNo!.Trim(),
            FullName = request.FullName!.Trim(),
            Email = request.Email!.Trim().ToLowerInvariant(),
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Students.Add(student);
        await SaveAsync(cancellationToken);
        _logger.LogInformation("Created student {StudentId}", student.Id);
        return ToResponse(student);
    }

    public async Task<StudentResponse?> UpdateAsync(int id, StudentWriteRequest request, CancellationToken cancellationToken)
    {
        var student = await _db.Students.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (student is null)
        {
            return null;
        }

        student.RegistrationNo = request.RegistrationNo!.Trim();
        student.FullName = request.FullName!.Trim();
        student.Email = request.Email!.Trim().ToLowerInvariant();
        await SaveAsync(cancellationToken);
        _logger.LogInformation("Updated student {StudentId}", student.Id);
        return ToResponse(student);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var student = await _db.Students.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (student is null)
        {
            return false;
        }

        _db.Students.Remove(student);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsPostgres(exception, PostgresErrorCodes.ForeignKeyViolation))
        {
            throw new ApiConflictException("Student has enrollments", "A student who is enrolled in a course cannot be deleted.");
        }

        _logger.LogInformation("Deleted student {StudentId}", id);
        return true;
    }

    public async Task<TranscriptResponse?> GetTranscriptAsync(int id, CancellationToken cancellationToken)
    {
        var student = await _db.Students.AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.Id, row.RegistrationNo, row.FullName })
            .FirstOrDefaultAsync(cancellationToken);
        if (student is null)
        {
            return null;
        }

        var passMark = _configuration.GetValue("Marks:PassMark", 50);
        var courses = await _db.Enrollments.AsNoTracking()
            .Where(enrollment => enrollment.StudentId == id)
            .OrderBy(enrollment => enrollment.Course.Code)
            .Select(enrollment => new TranscriptCourse
            {
                Code = enrollment.Course.Code,
                CreditHours = enrollment.Course.CreditHours,
                Mark = enrollment.Mark
            })
            .ToListAsync(cancellationToken);

        foreach (var course in courses)
        {
            course.Passed = course.Mark is int mark && mark >= passMark;
        }

        var graded = courses.Where(course => course.Mark is not null).ToList();
        double? weightedAverage = graded.Count == 0
            ? null
            : graded.Sum(course => course.Mark!.Value * course.CreditHours) / (double)graded.Sum(course => course.CreditHours);

        return new TranscriptResponse
        {
            StudentId = student.Id,
            RegistrationNo = student.RegistrationNo,
            FullName = student.FullName,
            Courses = courses,
            WeightedAverage = weightedAverage
        };
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsPostgres(exception, PostgresErrorCodes.UniqueViolation))
        {
            throw new ApiConflictException("Duplicate student", "A student with this registration number or email already exists.");
        }
    }

    private static bool IsPostgres(DbUpdateException exception, string code)
    {
        return exception.InnerException is PostgresException postgres && postgres.SqlState == code;
    }

    private static StudentResponse ToResponse(Student student)
    {
        return new StudentResponse
        {
            Id = student.Id,
            RegistrationNo = student.RegistrationNo,
            FullName = student.FullName,
            Email = student.Email
        };
    }
}

public class ApiConflictException : Exception
{
    public ApiConflictException(string title, string detail)
        : base(detail)
    {
        Title = title;
    }

    public string Title { get; }
}

public class ApiNotFoundException : Exception
{
    public ApiNotFoundException(string detail)
        : base(detail)
    {
    }
}