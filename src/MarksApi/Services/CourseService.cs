using MarksApi.Data;
using MarksApi.Data.Entities;
using MarksApi.Dtos;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace MarksApi.Services;

public interface ICourseService
{
    Task<PagedResult<CourseResponse>> ListAsync(int page, int pageSize, string? search, string? sort, CancellationToken cancellationToken);
    Task<CourseResponse?> GetAsync(int id, CancellationToken cancellationToken);
    Task<CourseResponse> CreateAsync(CourseWriteRequest request, CancellationToken cancellationToken);
    Task<CourseResponse?> UpdateAsync(int id, CourseWriteRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
    Task<CourseStatsResponse?> GetStatsAsync(int id, CancellationToken cancellationToken);
}

public class CourseService : ICourseService
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CourseService> _logger;

    public CourseService(AppDbContext db, IConfiguration configuration, ILogger<CourseService> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<PagedResult<CourseResponse>> ListAsync(int page, int pageSize, string? search, string? sort, CancellationToken cancellationToken)
    {
        (page, pageSize) = Normalize(page, pageSize);
        var query = _db.Courses.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLower();
            query = query.Where(course => course.Code.ToLower().Contains(term) || course.Title.ToLower().Contains(term));
        }

        query = sort switch
        {
            "title" => query.OrderBy(course => course.Title),
            "creditHours" => query.OrderBy(course => course.CreditHours),
            "createdAtUtc" => query.OrderBy(course => course.CreatedAtUtc),
            _ => query.OrderBy(course => course.Code)
        };

        var totalCount = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize)
            .Select(course => new CourseResponse
            {
                Id = course.Id,
                Code = course.Code,
                Title = course.Title,
                CreditHours = course.CreditHours
            })
            .ToListAsync(cancellationToken);

        return new PagedResult<CourseResponse> { Items = items, Page = page, PageSize = pageSize, TotalCount = totalCount };
    }

    public async Task<CourseResponse?> GetAsync(int id, CancellationToken cancellationToken)
    {
        return await _db.Courses.AsNoTracking()
            .Where(course => course.Id == id)
            .Select(course => new CourseResponse
            {
                Id = course.Id,
                Code = course.Code,
                Title = course.Title,
                CreditHours = course.CreditHours
            })
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<CourseResponse> CreateAsync(CourseWriteRequest request, CancellationToken cancellationToken)
    {
        var course = new Course
        {
            Code = request.Code!.Trim(),
            Title = request.Title!.Trim(),
            CreditHours = request.CreditHours!.Value,
            CreatedAtUtc = DateTime.UtcNow
        };
        _db.Courses.Add(course);
        await SaveAsync(cancellationToken);
        _logger.LogInformation("Created course {CourseId}", course.Id);
        return ToResponse(course);
    }

    public async Task<CourseResponse?> UpdateAsync(int id, CourseWriteRequest request, CancellationToken cancellationToken)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (course is null)
        {
            return null;
        }

        course.Code = request.Code!.Trim();
        course.Title = request.Title!.Trim();
        course.CreditHours = request.CreditHours!.Value;
        await SaveAsync(cancellationToken);
        _logger.LogInformation("Updated course {CourseId}", course.Id);
        return ToResponse(course);
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var course = await _db.Courses.FirstOrDefaultAsync(row => row.Id == id, cancellationToken);
        if (course is null)
        {
            return false;
        }

        _db.Courses.Remove(course);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsPostgres(exception, PostgresErrorCodes.ForeignKeyViolation))
        {
            throw new ApiConflictException("Course has enrollments", "A course that still has enrollments cannot be deleted.");
        }

        _logger.LogInformation("Deleted course {CourseId}", id);
        return true;
    }

    public async Task<CourseStatsResponse?> GetStatsAsync(int id, CancellationToken cancellationToken)
    {
        var course = await _db.Courses.AsNoTracking()
            .Where(row => row.Id == id)
            .Select(row => new { row.Id, row.Code })
            .FirstOrDefaultAsync(cancellationToken);
        if (course is null)
        {
            return null;
        }

        var marks = await _db.Enrollments.AsNoTracking()
            .Where(enrollment => enrollment.CourseId == id)
            .Select(enrollment => enrollment.Mark)
            .ToListAsync(cancellationToken);
        var graded = marks.Where(mark => mark.HasValue).Select(mark => mark!.Value).ToList();
        var passMark = _configuration.GetValue("Marks:PassMark", 50);

        return new CourseStatsResponse
        {
            CourseId = course.Id,
            CourseCode = course.Code,
            Enrolled = marks.Count,
            Graded = graded.Count,
            Average = graded.Count == 0 ? null : graded.Average(),
            Highest = graded.Count == 0 ? null : graded.Max(),
            Lowest = graded.Count == 0 ? null : graded.Min(),
            Passed = graded.Count(mark => mark >= passMark)
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
            throw new ApiConflictException("Duplicate course", "A course with this code already exists.");
        }
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

    private static CourseResponse ToResponse(Course course)
    {
        return new CourseResponse
        {
            Id = course.Id,
            Code = course.Code,
            Title = course.Title,
            CreditHours = course.CreditHours
        };
    }
}
