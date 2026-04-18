namespace MeetSpace.Client.Media.Models;

public sealed record ScreenSourceInfo(
    string Id,
    string Name,
    bool IsDisplay,
    bool IsWindow);