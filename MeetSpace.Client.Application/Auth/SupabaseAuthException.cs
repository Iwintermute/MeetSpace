namespace MeetSpace.Client.App.Auth;

public sealed class SupabaseAuthException : Exception
{
    public int? StatusCode { get; }

    public SupabaseAuthException(string message, int? statusCode = null)
        : base(message)
    {
        StatusCode = statusCode;
    }
}