using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeetSpace.Client.Shared.Results;

public class Result
{
    protected Result(bool isSuccess, Error? error)
    {
        if (isSuccess && error is not null)
            throw new ArgumentException("Successful result cannot contain error.", nameof(error));

        if (!isSuccess && error is null)
            throw new ArgumentException("Failed result must contain error.", nameof(error));

        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public Error? Error { get; }

    public static Result Success() => new(true, null);
    public static Result Failure(Error error) => new(false, error);
}

public sealed class Result<T> : Result
{
    private Result(bool isSuccess, T? value, Error? error)
        : base(isSuccess, error)
    {
        Value = value;
    }

    public T? Value { get; }

    public static Result<T> Success(T value) => new(true, value, null);
    public static new Result<T> Failure(Error error) => new(false, default, error);
}
