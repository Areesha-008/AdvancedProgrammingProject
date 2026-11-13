using MarksApi.Data;
using MarksApi.Data.Entities;
using MarksApi.Dtos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarksApi.Services;

public class StudentService : IStudentService
{
    private readonly AppDbContext _db;

    public StudentService(AppDbContext db)
    {
        _db = db;
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
            throw new StudentConflictException("Student has enrollments", "A student who is enrolled in a course cannot be deleted.");
        }

        return true;
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsPostgres(exception, PostgresErrorCodes.UniqueViolation))
        {
            throw new StudentConflictException("Duplicate student", "A student with this registration number or email already exists.");
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

public class StudentConflictException : Exception
{
    public StudentConflictException(string title, string detail)
        : base(detail)
    {
        Title = title;
    }

    public string Title { get; }
}