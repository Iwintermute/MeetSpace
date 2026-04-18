namespace MeetSpace.Client.Media.Models;

public sealed record AudioInputDeviceInfo(
    string Id,
    string Name,
    bool IsDefault);