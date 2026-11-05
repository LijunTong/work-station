namespace WorkStation.Core;

/// <summary>错误信息：错误码机器可读，message 是可直接展示给用户的中文文案。</summary>
public sealed record ErrorInfo(string Code, string Message);

/// <summary>
/// 服务层统一返回类型。契约沿用设计文档 §8：永远不向外 throw，
/// 失败一律转成 { ok:false, error }。
/// </summary>
public sealed record Result<T>
{
    public bool Ok { get; init; }

    public T? Data { get; init; }

    public ErrorInfo? Error { get; init; }

    public static Result<T> Success(T data) => new() { Ok = true, Data = data };

    public static Result<T> Failure(string code, string message) =>
        new() { Ok = false, Error = new ErrorInfo(code, message) };
}

/// <summary>错误码常量。</summary>
public static class ErrorCodes
{
    public const string HandlerFailed = "HANDLER_FAILED";

    public const string DbWriteFailed = "DB_WRITE_FAILED";

    public const string NotFound = "NOT_FOUND";

    public const string ValidationFailed = "VALIDATION_FAILED";
}
