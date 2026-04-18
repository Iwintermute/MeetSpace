namespace MeetSpace.Client.Media.Models;

public sealed record AudioOutputDeviceInfo(
    string Id,
    string Name,
    bool IsDefault);