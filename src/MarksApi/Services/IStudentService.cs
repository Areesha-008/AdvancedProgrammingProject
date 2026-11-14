using MarksApi.Dtos;

namespace MarksApi.Services;

public interface IStudentService
{
    Task<PagedResult<StudentResponse>> ListAsync(int page, int pageSize, string? search, string? sort, CancellationToken cancellationToken);
    Task<StudentResponse?> GetAsync(int id, CancellationToken cancellationToken);
    Task<StudentResponse> CreateAsync(StudentWriteRequest request, CancellationToken cancellationToken);
    Task<StudentResponse?> UpdateAsync(int id, StudentWriteRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
    Task<TranscriptResponse?> GetTranscriptAsync(int id, CancellationToken cancellationToken);
}