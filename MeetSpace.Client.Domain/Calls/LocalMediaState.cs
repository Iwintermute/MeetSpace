using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Domain.Calls;

public sealed record LocalMediaState(
    bool MicrophoneEnabled,
    bool CameraEnabled,
    bool ScreenShareEnabled,
    string? ActiveMicrophoneId = null,
    string? ActiveCameraId = null,
    string? ActiveScreenSourceId = null);