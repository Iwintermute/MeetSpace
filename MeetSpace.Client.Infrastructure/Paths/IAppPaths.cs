namespace MeetSpace.Client.Infrastructure.Paths;

public interface IAppPaths
{
    string AppDataDirectory { get; }
    string LogsDirectory { get; }
    string SettingsDirectory { get; }
}