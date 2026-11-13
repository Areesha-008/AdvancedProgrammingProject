using MarksApi.Dtos;
namespace MarksApi.Services;


public interface IAuthService{
    Task<LoginResponse?> LoginAsync(string username, string password, CancellationToken cancellationToken);
}