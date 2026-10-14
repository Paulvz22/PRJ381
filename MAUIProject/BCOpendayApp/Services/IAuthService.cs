namespace BCOpendayApp.Services;

public interface IAuthService
{
    Task<bool> EnsureSignedInAsync();
    string? CurrentUserId { get; }
}
