using MarksApi.Dtos;

namespace MarksApi.Services;

public interface IStudentService
{
    Task<PagedResult<StudentResponse>> ListAsync(int page, int pageSize, string? search, string? sort, CancellationToken cancellationToken);
    Task<StudentResponse?> GetAsync(int id, CancellationToken cancellationToken);
}