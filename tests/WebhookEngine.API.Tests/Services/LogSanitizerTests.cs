using FluentAssertions;
using WebhookEngine.Core.Utilities;

namespace WebhookEngine.API.Tests.Services;

public class LogSanitizerTests
{
    [Fact]
    public void ForLog_Strips_CarriageReturn_And_Newline()
    {
        var malicious = "GET /path\r\nINFO: fake log entry";

        LogSanitizer.ForLog(malicious).Should().Be("GET /pathINFO: fake log entry");
    }

    [Fact]
    public void ForLog_Replaces_Tab_With_Space()
    {
        LogSanitizer.ForLog("a\tb\tc").Should().Be("a b c");
    }

    [Fact]
    public void ForLog_Caps_Length_Even_When_No_Control_Chars()
    {
        var longInput = new string('x', 1000);

        LogSanitizer.ForLog(longInput).Should().HaveLength(256);
    }

    [Fact]
    public void ForLog_Returns_Empty_For_Null()
    {
        LogSanitizer.ForLog(null).Should().Be(string.Empty);
    }

    [Fact]
    public void ForLog_Returns_Empty_For_Empty()
    {
        LogSanitizer.ForLog("").Should().Be(string.Empty);
    }

    [Fact]
    public void ForLog_Passes_Through_Safe_Input()
    {
        LogSanitizer.ForLog("/api/v1/messages").Should().Be("/api/v1/messages");
    }

    [Fact]
    public void RedactEmail_Keeps_First_Char_And_Domain()
    {
        LogSanitizer.RedactEmail("alice@example.com").Should().Be("a***@example.com");
    }

    [Fact]
    public void RedactEmail_Returns_Redacted_For_Missing_AtSign()
    {
        LogSanitizer.RedactEmail("not-an-email").Should().Be("<redacted>");
    }

    [Fact]
    public void RedactEmail_Returns_Redacted_For_Trailing_AtSign()
    {
        LogSanitizer.RedactEmail("user@").Should().Be("<redacted>");
    }

    [Fact]
    public void RedactEmail_Returns_Redacted_For_Leading_AtSign()
    {
        LogSanitizer.RedactEmail("@example.com").Should().Be("<redacted>");
    }

    [Fact]
    public void RedactEmail_Returns_Empty_Marker_For_Null()
    {
        LogSanitizer.RedactEmail(null).Should().Be("<empty>");
    }

    [Fact]
    public void RedactEmail_Single_Char_Local_Part_Still_Hides_Identity()
    {
        LogSanitizer.RedactEmail("a@b.co").Should().Be("a***@b.co");
    }
}
