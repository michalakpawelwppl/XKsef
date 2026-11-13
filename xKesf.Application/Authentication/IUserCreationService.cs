namespace xKsef.Application.Authentication;

public interface IUserCreationService
{
    Task<CreateUserResult> CreateAsync(string login, string email, string password, CancellationToken cancellationToken = default);
}
