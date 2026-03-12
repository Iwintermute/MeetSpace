using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Security.Models;

public sealed record EncryptedPayload(
    string Algorithm,
    string CipherTextBase64,
    string IvBase64,
    string? TagBase64,
    Dictionary<string, string>? Metadata = null);