using System.Text;
using DevLab.JmesPath;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WebhookEngine.Core.Interfaces;
using WebhookEngine.Core.Options;

namespace WebhookEngine.Infrastructure.Services;

/// <summary>
/// JMESPath-backed payload transformer (ADR-003). Wraps the JmesPath.Net library
/// with a hard timeout, an output-size guard, and a fail-open contract: any
/// error returns <see cref="PayloadTransformResult.FailOpen"/> so callers fall
/// back to the original payload. Stateless and safe to register as a singleton.
/// </summary>
public sealed class JmesPathPayloadTransformer : IPayloadTransformer
{
    private readonly TransformationOptions _options;
    private readonly ILogger<JmesPathPayloadTransformer> _logger;
    private readonly JmesPath _jmesPath = new();

    public JmesPathPayloadTransformer(
        IOptions<TransformationOptions> options,
        ILogger<JmesPathPayloadTransformer> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public PayloadTransformResult Transform(string expression, string payload)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return PayloadTransformResult.FailOpen("Expression is empty.");
        }

        // Run the JMESPath evaluation on a thread-pool task so we can enforce a
        // hard wall-clock timeout even if the expression hits a pathological
        // pattern that the parser does not detect ahead of time.
        var task = Task.Run(() => _jmesPath.Transform(payload, expression));

        try
        {
            if (!task.Wait(TimeSpan.FromMilliseconds(_options.TimeoutMs)))
            {
                _logger.LogWarning(
                    "JMESPath transformation timed out after {TimeoutMs}ms for expression {Expression}",
                    _options.TimeoutMs, expression);
                return PayloadTransformResult.FailOpen($"Timeout after {_options.TimeoutMs}ms.");
            }
        }
        catch (AggregateException ex) when (ex.InnerException is not null)
        {
            _logger.LogWarning(
                ex.InnerException,
                "JMESPath transformation failed for expression {Expression}",
                expression);
            return PayloadTransformResult.FailOpen(ex.InnerException.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "JMESPath transformation failed for expression {Expression}", expression);
            return PayloadTransformResult.FailOpen(ex.Message);
        }

        var transformed = task.Result;
        if (transformed is null)
        {
            return PayloadTransformResult.FailOpen("JMESPath returned null result.");
        }

        // Reject results that exceed the output budget to keep delivery payloads
        // bounded. UTF-8 byte length matches what the HTTP body will weigh.
        var byteCount = Encoding.UTF8.GetByteCount(transformed);
        if (byteCount > _options.MaxOutputBytes)
        {
            _logger.LogWarning(
                "JMESPath transformation output exceeded {MaxOutputBytes} bytes ({ActualBytes}) for expression {Expression}",
                _options.MaxOutputBytes, byteCount, expression);
            return PayloadTransformResult.FailOpen(
                $"Output size {byteCount} exceeds limit {_options.MaxOutputBytes}.");
        }

        return PayloadTransformResult.Success(transformed);
    }
}
