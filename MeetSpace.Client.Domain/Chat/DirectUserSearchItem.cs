namespace MeetSpace.Client.Domain.Chat;

public sealed record DirectUserSearchItem(
    string UserId,
    string Email,
    string? DisplayName = null)
{
    public string Title => string.IsNullOrWhiteSpace(DisplayName) ? Email : DisplayName!;
}
