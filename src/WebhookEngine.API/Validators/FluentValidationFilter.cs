using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using WebhookEngine.API.Contracts;

namespace WebhookEngine.API.Validators;

/// <summary>
/// Action filter that automatically validates request models using FluentValidation.
/// Replaces the deprecated FluentValidation.AspNetCore auto-validation.
/// </summary>
public class FluentValidationFilter : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        foreach (var (_, value) in context.ActionArguments)
        {
            if (value is null) continue;

            var validatorType = typeof(IValidator<>).MakeGenericType(value.GetType());
            if (context.HttpContext.RequestServices.GetService(validatorType) is not IValidator validator)
                continue;

            var validationContext = new ValidationContext<object>(value);
            var result = await validator.ValidateAsync(validationContext, context.HttpContext.RequestAborted);

            if (!result.IsValid)
            {
                var errors = result.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(e => e.ErrorMessage).ToArray());

                context.Result = new UnprocessableEntityObjectResult(
                    ApiEnvelope.Error(context.HttpContext, "VALIDATION_ERROR", "One or more validation errors occurred.", errors));
                return;
            }
        }

        await next();
    }
}
