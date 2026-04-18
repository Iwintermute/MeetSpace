namespace MeetSpace.Client.Media.Models;

public sealed record CameraDeviceInfo(
    string Id,
    string Name,
    bool IsDefault);