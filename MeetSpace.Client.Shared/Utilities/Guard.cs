using System;
namespace MeetSpace.Client.Shared.Utilities;

public static class Guard
{
    public static T NotNull<T>(T? value, string paramName) where T : class
    {
        if (value == null)
            throw new ArgumentNullException(paramName);

        return value;
    }

    public static string NotNullOrWhiteSpace(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value must not be null or whitespace.", paramName);

        return value;
    }
}