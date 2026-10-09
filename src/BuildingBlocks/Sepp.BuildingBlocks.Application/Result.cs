namespace Sepp.BuildingBlocks.Application;

public enum ErrorKind
{
    Validation,
    NotFound,
    Conflict,
    Forbidden,
    Unprocessable,
}

/// <summary>Erreur fonctionnelle attendue, traduite en ProblemDetails par l'adaptateur HTTP.</summary>
public sealed record Error(ErrorKind Kind, string Code, string Message)
{
    public static Error Validation(string code, string message) => new(ErrorKind.Validation, code, message);

    public static Error NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);

    public static Error Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);

    public static Error Forbidden(string code, string message) => new(ErrorKind.Forbidden, code, message);

    /// <summary>Demande bien formée mais contraire à l'état métier connu (HTTP 422).</summary>
    public static Error Unprocessable(string code, string message) => new(ErrorKind.Unprocessable, code, message);
}

public readonly record struct Unit
{
    public static readonly Unit Value;
}

/// <summary>Résultat d'un cas d'usage : une valeur ou une erreur fonctionnelle.</summary>
public sealed class Result<T>
{
    private readonly T? _value;

    private Result(T value)
    {
        _value = value;
        IsSuccess = true;
    }

    private Result(Error error)
    {
        Error = error;
    }

    public bool IsSuccess { get; }

    public Error? Error { get; }

    public T Value => IsSuccess ? _value! : throw new InvalidOperationException($"Résultat en échec : {Error!.Code}.");

    public static Result<T> Success(T value) => new(value);

    public static Result<T> Failure(Error error) => new(error);

    public static implicit operator Result<T>(T value) => Success(value);

    public static implicit operator Result<T>(Error error) => Failure(error);

    public TOut Match<TOut>(Func<T, TOut> onSuccess, Func<Error, TOut> onFailure) =>
        IsSuccess ? onSuccess(_value!) : onFailure(Error!);
}
