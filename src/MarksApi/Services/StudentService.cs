using MarksApi.Data;
using MarksApi.Dtos;
using Microsoft.EntityFrameworkCore;

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
}