namespace Shop.BuildingBlocks.Abstractions;

public interface ICurrentUser
{
    string UserId { get; }
    string DisplayName { get; }
    bool HasScope(string scope);
    bool IsAuthenticated { get; }
}
