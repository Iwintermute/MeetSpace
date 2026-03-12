using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Infrastructure.Paths;

public interface IAppPaths
{
    string AppDataDirectory { get; }
    string LogsDirectory { get; }
    string SettingsDirectory { get; }
}