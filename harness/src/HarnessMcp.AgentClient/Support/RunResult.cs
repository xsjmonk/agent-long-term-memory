using System.Collections.Generic;

namespace HarnessMcp.AgentClient.Support;

public sealed class RunResult<T>
{
    private RunResult(bool isSuccess, T? value, IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        IsSuccess = isSuccess;
        Value = value;
        Errors = errors;
        Warnings = warnings;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public IReadOnlyList<string> Errors { get; }
    public IReadOnlyList<string> Warnings { get; }

    public static RunResult<T> Success(T value, IReadOnlyList<string>? warnings = null) =>
        new(true, value, Array.Empty<string>(), warnings ?? Array.Empty<string>());

    public static RunResult<T> Failure(string error, IReadOnlyList<string>? warnings = null) =>
        new(false, default, new[] { error }, warnings ?? Array.Empty<string>());

    public static RunResult<T> Failure(IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null) =>
        new(false, default, errors, warnings ?? Array.Empty<string>());
}

public static class RunResult
{
    public static RunResult<T> Success<T>(T value, IReadOnlyList<string>? warnings = null) => RunResult<T>.Success(value, warnings);

    public static RunResult<T> Failure<T>(string error, IReadOnlyList<string>? warnings = null) => RunResult<T>.Failure(error, warnings);

    public static RunResult<T> Failure<T>(IReadOnlyList<string> errors, IReadOnlyList<string>? warnings = null) =>
        RunResult<T>.Failure(errors, warnings);
}

