using FluentValidation;
using WebhookEngine.API.Controllers;
using WebhookEngine.API.Services;

namespace WebhookEngine.API.Validators;

public class CreateApplicationRequestValidator : AbstractValidator<CreateApplicationRequest>
{
    public CreateApplicationRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(255);
    }
}

public class UpdateApplicationRequestValidator : AbstractValidator<UpdateApplicationRequest>
{
    public UpdateApplicationRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(255)
            .When(x => x.Name is not null);

        RuleFor(x => x.IdempotencyWindowMinutes)
            .InclusiveBetween(1, 10080)
            .When(x => x.IdempotencyWindowMinutes.HasValue);

        RuleFor(x => x)
            .Must(x => x.Name is not null || x.IsActive.HasValue || x.IdempotencyWindowMinutes.HasValue)
            .WithMessage("At least one field must be provided.");
    }
}

public class CreateEventTypeRequestValidator : AbstractValidator<CreateEventTypeRequest>
{
    public CreateEventTypeRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);
    }
}

public class UpdateEventTypeRequestValidator : AbstractValidator<UpdateEventTypeRequest>
{
    public UpdateEventTypeRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(255)
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);

        RuleFor(x => x)
            .Must(x => x.Name is not null || x.Description is not null || x.Schema is not null)
            .WithMessage("At least one field must be provided.");
    }
}

public class CreateEndpointRequestValidator : AbstractValidator<CreateEndpointRequest>
{
    public CreateEndpointRequestValidator(EndpointUrlPolicy urlPolicy)
    {
        RuleFor(x => x.Url)
            .NotEmpty()
            .Must(urlPolicy.IsValid)
            .WithMessage(urlPolicy.ValidationMessage);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);

        RuleFor(x => x.TransformExpression)
            .MaximumLength(4096)
            .When(x => x.TransformExpression is not null)
            .WithMessage("TransformExpression must not exceed 4096 characters.");

        RuleFor(x => x.CustomHeaders)
            .Must(headers => CustomHeaderPolicy.Validate(headers) is null)
            .WithMessage(x => CustomHeaderPolicy.Validate(x.CustomHeaders) ?? "Invalid custom headers.")
            .When(x => x.CustomHeaders is not null);
    }
}

public class UpdateEndpointRequestValidator : AbstractValidator<UpdateEndpointRequest>
{
    public UpdateEndpointRequestValidator(EndpointUrlPolicy urlPolicy)
    {
        RuleFor(x => x.Url)
            .Must(urlPolicy.IsValid)
            .When(x => x.Url is not null)
            .WithMessage(urlPolicy.ValidationMessage);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);

        RuleFor(x => x.TransformExpression)
            .MaximumLength(4096)
            .When(x => x.TransformExpression is not null)
            .WithMessage("TransformExpression must not exceed 4096 characters.");

        RuleFor(x => x.CustomHeaders)
            .Must(headers => CustomHeaderPolicy.Validate(headers) is null)
            .WithMessage(x => CustomHeaderPolicy.Validate(x.CustomHeaders) ?? "Invalid custom headers.")
            .When(x => x.CustomHeaders is not null);

        RuleFor(x => x)
            .Must(x => x.Url is not null
                || x.Description is not null
                || x.FilterEventTypes is not null
                || x.CustomHeaders is not null
                || x.Metadata is not null
                || x.SecretOverride is not null
                || x.TransformExpression is not null
                || x.TransformEnabled is not null)
            .WithMessage("At least one field must be provided.");
    }
}

public class DashboardCreateEndpointRequestValidator : AbstractValidator<DashboardCreateEndpointRequest>
{
    public DashboardCreateEndpointRequestValidator(EndpointUrlPolicy urlPolicy)
    {
        RuleFor(x => x.AppId)
            .NotEmpty();

        RuleFor(x => x.Url)
            .NotEmpty()
            .Must(urlPolicy.IsValid)
            .WithMessage(urlPolicy.ValidationMessage);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);

        RuleFor(x => x.TransformExpression)
            .MaximumLength(4096)
            .When(x => x.TransformExpression is not null)
            .WithMessage("TransformExpression must not exceed 4096 characters.");

        RuleFor(x => x.CustomHeaders)
            .Must(headers => CustomHeaderPolicy.Validate(headers) is null)
            .WithMessage(x => CustomHeaderPolicy.Validate(x.CustomHeaders) ?? "Invalid custom headers.")
            .When(x => x.CustomHeaders is not null);
    }
}

public class DashboardUpdateEndpointRequestValidator : AbstractValidator<DashboardUpdateEndpointRequest>
{
    public DashboardUpdateEndpointRequestValidator(EndpointUrlPolicy urlPolicy)
    {
        RuleFor(x => x.Url)
            .Must(urlPolicy.IsValid)
            .When(x => x.Url is not null)
            .WithMessage(urlPolicy.ValidationMessage);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);

        RuleFor(x => x.TransformExpression)
            .MaximumLength(4096)
            .When(x => x.TransformExpression is not null)
            .WithMessage("TransformExpression must not exceed 4096 characters.");

        RuleFor(x => x.CustomHeaders)
            .Must(headers => CustomHeaderPolicy.Validate(headers) is null)
            .WithMessage(x => CustomHeaderPolicy.Validate(x.CustomHeaders) ?? "Invalid custom headers.")
            .When(x => x.CustomHeaders is not null);

        RuleFor(x => x)
            .Must(x => x.Url is not null
                || x.Description is not null
                || x.FilterEventTypes is not null
                || x.CustomHeaders is not null
                || x.Metadata is not null
                || x.SecretOverride is not null
                || x.TransformExpression is not null
                || x.TransformEnabled is not null)
            .WithMessage("At least one field must be provided.");
    }
}

public class DashboardCreateEventTypeRequestValidator : AbstractValidator<DashboardCreateEventTypeRequest>
{
    public DashboardCreateEventTypeRequestValidator()
    {
        RuleFor(x => x.AppId)
            .NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);
    }
}

public class DashboardUpdateEventTypeRequestValidator : AbstractValidator<DashboardUpdateEventTypeRequest>
{
    public DashboardUpdateEventTypeRequestValidator()
    {
        RuleFor(x => x.Name)
            .MaximumLength(255)
            .When(x => x.Name is not null);

        RuleFor(x => x.Description)
            .MaximumLength(500)
            .When(x => x.Description is not null);

        RuleFor(x => x)
            .Must(x => x.Name is not null || x.Description is not null)
            .WithMessage("At least one field must be provided.");
    }
}

public class ValidateTransformRequestValidator : AbstractValidator<ValidateTransformRequest>
{
    public ValidateTransformRequestValidator()
    {
        RuleFor(x => x.Expression)
            .NotEmpty()
            .MaximumLength(4096)
            .WithMessage("Expression must be 1-4096 characters.");

        RuleFor(x => x.SamplePayload)
            .NotEmpty()
            .Must(payload => System.Text.Encoding.UTF8.GetByteCount(payload) <= 65536)
            .WithMessage("Sample payload must not exceed 64 KB.");
    }
}

public class SendMessageRequestValidator : AbstractValidator<SendMessageRequest>
{
    public SendMessageRequestValidator()
    {
        RuleFor(x => x.EventType)
            .MaximumLength(255);

        RuleFor(x => x.EventTypeId)
            .NotEmpty()
            .When(x => x.EventTypeId.HasValue);

        RuleFor(x => x)
            .Must(x => x.EventTypeId.HasValue || !string.IsNullOrWhiteSpace(x.EventType))
            .WithMessage("eventType or eventTypeId must be provided.");

        RuleFor(x => x.Payload)
            .NotNull();

        RuleFor(x => x.EventId)
            .MaximumLength(64)
            .When(x => x.EventId is not null);

        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(128)
            .When(x => x.IdempotencyKey is not null);
    }
}

public class BatchSendMessagesRequestValidator : AbstractValidator<BatchSendMessagesRequest>
{
    public BatchSendMessagesRequestValidator()
    {
        RuleFor(x => x.Messages)
            .NotNull()
            .NotEmpty()
            .Must(messages => messages.Count <= 100)
            .WithMessage("messages must contain between 1 and 100 items.");

        RuleForEach(x => x.Messages)
            .SetValidator(new SendMessageRequestValidator());
    }
}

public class DashboardSendMessageRequestValidator : AbstractValidator<DashboardSendMessageRequest>
{
    public DashboardSendMessageRequestValidator()
    {
        RuleFor(x => x.AppId)
            .NotEmpty();

        RuleFor(x => x.EventType)
            .MaximumLength(255);

        RuleFor(x => x.EventTypeId)
            .NotEmpty()
            .When(x => x.EventTypeId.HasValue);

        RuleFor(x => x)
            .Must(x => x.EventTypeId.HasValue || !string.IsNullOrWhiteSpace(x.EventType))
            .WithMessage("eventType or eventTypeId must be provided.");

        RuleFor(x => x.Payload)
            .NotNull();

        RuleFor(x => x.EventId)
            .MaximumLength(64)
            .When(x => x.EventId is not null);

        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(128)
            .When(x => x.IdempotencyKey is not null);
    }
}

public class ReplayMessagesRequestValidator : AbstractValidator<ReplayMessagesRequest>
{
    public ReplayMessagesRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => x.EventTypeId.HasValue || !string.IsNullOrWhiteSpace(x.EventType))
            .WithMessage("eventType or eventTypeId must be provided.");

        RuleFor(x => x.EventType)
            .MaximumLength(255)
            .When(x => x.EventType is not null);

        RuleFor(x => x.From)
            .NotEmpty();

        RuleFor(x => x.To)
            .NotEmpty();

        RuleFor(x => x)
            .Must(x => x.From <= x.To)
            .WithMessage("from must be less than or equal to to.");

        RuleFor(x => x.MaxMessages)
            .InclusiveBetween(1, 1000);

        RuleFor(x => x.Statuses)
            .Must(statuses => statuses is null || statuses.Count > 0)
            .WithMessage("statuses cannot be empty when provided.");
    }
}

public class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty()
            .EmailAddress();

        RuleFor(x => x.Password)
            .NotEmpty();
    }
}

public class DevTrafficStartRequestValidator : AbstractValidator<DevTrafficStartRequest>
{
    public DevTrafficStartRequestValidator()
    {
        RuleFor(x => x.IntervalMs)
            .InclusiveBetween(250, 60_000);

        RuleFor(x => x.MessagesPerTick)
            .InclusiveBetween(1, 25);
    }
}

public class DevTrafficSeedRequestValidator : AbstractValidator<DevTrafficSeedRequest>
{
    public DevTrafficSeedRequestValidator()
    {
        RuleFor(x => x.Messages)
            .InclusiveBetween(1, 50);
    }
}
