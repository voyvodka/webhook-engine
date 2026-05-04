namespace WebhookEngine.Core.Interfaces;

/// <summary>
/// Applies a declarative payload transformation (JMESPath expression) to a JSON payload
/// before delivery. Implementations must be fail-safe: any error during transformation
/// returns <see cref="PayloadTransformResult.FailOpen"/> so the caller can fall back to
/// the original payload, never blocking delivery (ADR-003).
/// </summary>
public interface IPayloadTransformer
{
    /// <summary>
    /// Apply <paramref name="expression"/> to <paramref name="payload"/> with built-in
    /// timeout and output-size guards. Always returns a result; check
    /// <see cref="PayloadTransformResult.IsSuccess"/> to decide whether to use the
    /// transformed payload or fall back to the original.
    /// </summary>
    PayloadTransformResult Transform(string expression, string payload);
}

public sealed record PayloadTransformResult(bool IsSuccess, string? TransformedPayload, string? Error)
{
    public static PayloadTransformResult Success(string transformed) => new(true, transformed, null);
    public static PayloadTransformResult FailOpen(string error) => new(false, null, error);
}
