using FluentAssertions;
using WebhookEngine.API.Auth;

namespace WebhookEngine.API.Tests.Auth;

public class PasswordHasherTests
{
    [Fact]
    public void HashPassword_Returns_Salt_Colon_Hash_Format()
    {
        var hash = PasswordHasher.HashPassword("mypassword");

        hash.Should().Contain(":");
        var parts = hash.Split(':');
        parts.Should().HaveCount(2);

        // Both parts should be valid Base64
        var act1 = () => Convert.FromBase64String(parts[0]);
        var act2 = () => Convert.FromBase64String(parts[1]);
        act1.Should().NotThrow();
        act2.Should().NotThrow();
    }

    [Fact]
    public void HashPassword_Generates_Unique_Hashes_For_Same_Password()
    {
        var hash1 = PasswordHasher.HashPassword("password123");
        var hash2 = PasswordHasher.HashPassword("password123");

        // Different salts produce different hashes
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void VerifyPassword_Returns_True_For_Correct_Password()
    {
        var password = "correct-horse-battery-staple";
        var hash = PasswordHasher.HashPassword(password);

        PasswordHasher.VerifyPassword(password, hash).Should().BeTrue();
    }

    [Fact]
    public void VerifyPassword_Returns_False_For_Wrong_Password()
    {
        var hash = PasswordHasher.HashPassword("correct-password");

        PasswordHasher.VerifyPassword("wrong-password", hash).Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_Returns_False_For_Invalid_Hash_Format()
    {
        PasswordHasher.VerifyPassword("any-password", "invalid-hash-no-colon").Should().BeFalse();
    }

    [Fact]
    public void VerifyPassword_Returns_False_For_Empty_Hash()
    {
        PasswordHasher.VerifyPassword("any-password", "").Should().BeFalse();
    }

    [Fact]
    public void HashPassword_Salt_Is_16_Bytes()
    {
        var hash = PasswordHasher.HashPassword("test");
        var salt = Convert.FromBase64String(hash.Split(':')[0]);

        salt.Should().HaveCount(16);
    }

    [Fact]
    public void HashPassword_Hash_Is_32_Bytes()
    {
        var hash = PasswordHasher.HashPassword("test");
        var derivedKey = Convert.FromBase64String(hash.Split(':')[1]);

        derivedKey.Should().HaveCount(32);
    }
}
