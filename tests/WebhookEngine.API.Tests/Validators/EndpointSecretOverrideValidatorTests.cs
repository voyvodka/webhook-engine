using FluentValidation.TestHelper;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using WebhookEngine.API.Controllers;
using WebhookEngine.API.Validators;
using WebhookEngine.Core.Options;

namespace WebhookEngine.API.Tests.Validators;

// B5: the `.EndpointSecretOverride()` rule (whsec_ prefix + length 32..64) now
// guards the three surfaces that previously accepted an arbitrary override. Each
// class pins the same matrix: 1-char fails, valid passes, >64 fails, null passes.
internal static class SecretOverrideCases
{
    // 6-char prefix + 34 = 40 chars, comfortably inside [32, 64].
    public const string Valid = "whsec_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    // 6-char prefix + 60 = 66 chars, one over the varchar(64) ceiling.
    public const string TooLong = "whsec_aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    public const string Short = "x";

    public static EndpointUrlPolicy BuildUrlPolicy()
    {
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Production);
        return new EndpointUrlPolicy(env, Options.Create(new SsrfGuardOptions()));
    }
}

public class UpdateEndpointRequestValidatorSecretOverrideTests
{
    private readonly UpdateEndpointRequestValidator _validator = new(SecretOverrideCases.BuildUrlPolicy());

    [Fact]
    public async Task SecretOverride_One_Char_Fails_Validation()
    {
        var result = await _validator.TestValidateAsync(new UpdateEndpointRequest { SecretOverride = SecretOverrideCases.Short });

        result.ShouldHaveValidationErrorFor(x => x.SecretOverride);
    }

    [Fact]
    public async Task SecretOverride_Valid_Whsec_Prefixed_Passes()
    {
        var result = await _validator.TestValidateAsync(new UpdateEndpointRequest { SecretOverride = SecretOverrideCases.Valid });

        result.ShouldNotHaveValidationErrorFor(x => x.SecretOverride);
    }

    [Fact]
    public async Task SecretOverride_Over_64_Chars_Fails_Validation()
    {
        var result = await _validator.TestValidateAsync(new UpdateEndpointRequest { SecretOverride = SecretOverrideCases.TooLong });

        result.ShouldHaveValidationErrorFor(x => x.SecretOverride);
    }

    [Fact]
    public async Task SecretOverride_Null_Skips_The_Rule()
    {
        var result = await _validator.TestValidateAsync(new UpdateEndpointRequest { Description = "still one field set" });

        result.ShouldNotHaveValidationErrorFor(x => x.SecretOverride);
    }
}

public class DashboardCreateEndpointRequestValidatorSecretOverrideTests
{
    private readonly DashboardCreateEndpointRequestValidator _validator = new(SecretOverrideCases.BuildUrlPolicy());

    [Fact]
    public async Task SecretOverride_One_Char_Fails_Validation()
    {
        var result = await _validator.TestValidateAsync(new DashboardCreateEndpointRequest { SecretOverride = SecretOverrideCases.Short });

        result.ShouldHaveValidationErrorFor(x => x.SecretOverride);
    }

    [Fact]
    public async Task SecretOverride_Valid_Whsec_Prefixed_Passes()
    {
        var result = await _validator.TestValidateAsync(new DashboardCreateEndpointRequest { SecretOverride = SecretOverrideCases.Valid });

        result.ShouldNotHaveValidationErrorFor(x => x.SecretOverride);
    }

    [Fact]
    public async Task SecretOverride_Over_64_Chars_Fails_Validation()
    {
        var result = await _validator.TestValidateAsync(new DashboardCreateEndpointRequest { SecretOverride = SecretOverrideCases.TooLong });

        result.ShouldHaveValidationErrorFor(x => x.SecretOverride);
    }

    [Fact]
    public async Task SecretOverride_Null_Skips_The_Rule()
    {
        var result = await _validator.TestValidateAsync(new DashboardCreateEndpointRequest());

        result.ShouldNotHaveValidationErrorFor(x => x.SecretOverride);
    }
}

public class DashboardUpdateEndpointRequestValidatorSecretOverrideTests
{
    private readonly DashboardUpdateEndpointRequestValidator _validator = new(SecretOverrideCases.BuildUrlPolicy());

    [Fact]
    public async Task SecretOverride_One_Char_Fails_Validation()
    {
        var result = await _validator.TestValidateAsync(new DashboardUpdateEndpointRequest { SecretOverride = SecretOverrideCases.Short });

        result.ShouldHaveValidationErrorFor(x => x.SecretOverride);
    }

    [Fact]
    public async Task SecretOverride_Valid_Whsec_Prefixed_Passes()
    {
        var result = await _validator.TestValidateAsync(new DashboardUpdateEndpointRequest { SecretOverride = SecretOverrideCases.Valid });

        result.ShouldNotHaveValidationErrorFor(x => x.SecretOverride);
    }

    [Fact]
    public async Task SecretOverride_Over_64_Chars_Fails_Validation()
    {
        var result = await _validator.TestValidateAsync(new DashboardUpdateEndpointRequest { SecretOverride = SecretOverrideCases.TooLong });

        result.ShouldHaveValidationErrorFor(x => x.SecretOverride);
    }

    [Fact]
    public async Task SecretOverride_Null_Skips_The_Rule()
    {
        var result = await _validator.TestValidateAsync(new DashboardUpdateEndpointRequest { Description = "still one field set" });

        result.ShouldNotHaveValidationErrorFor(x => x.SecretOverride);
    }
}
