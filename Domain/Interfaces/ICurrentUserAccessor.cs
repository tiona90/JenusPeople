namespace Domain.Interfaces;

public interface ICurrentUserAccessor
{
    string? UserId { get; }
}
