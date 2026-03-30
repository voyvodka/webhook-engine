# Testing Patterns

**Analysis Date:** 2026-03-30

## Test Framework

**Runner:**
- xUnit 2.9.3
- Config: `.csproj` files specify `<IsPackable>false</IsPackable>` and `<TargetFramework>net10.0</TargetFramework>`

**Assertion Library:**
- FluentAssertions 8.8.0 — provides fluent syntax for assertions

**Coverage:**
- coverlet.collector 8.0.0 included in all test projects
- Requirements: Not enforced; no coverage threshold specified in codebase

**Run Commands:**

```bash
dotnet test                          # Run all tests in solution
dotnet test --configuration Release  # Run tests in Release mode
dotnet test --verbosity detailed     # Run with detailed output
dotnet test WebhookEngine.Core.Tests # Run specific test project
```

## Test File Organization

**Location:**
- Co-located in parallel structure: `/tests/WebhookEngine.Core.Tests/` mirrors `/src/WebhookEngine.Core/`
- Test directory per domain: `Entities/`, `Services/`, `Repositories/`, `Options/`, `Models/`, `Enums/`

**Naming:**
- Suffix: `Tests.cs` (e.g., `ApplicationEntityTests.cs`, `HmacSigningServiceTests.cs`)
- Test class names match entity/service being tested plus `Tests`
- Test projects: `WebhookEngine.Core.Tests`, `WebhookEngine.Infrastructure.Tests`, `WebhookEngine.API.Tests`, `WebhookEngine.Worker.Tests`, `WebhookEngine.Application.Tests`

**Structure:**
```
tests/
├── WebhookEngine.Core.Tests/
│   ├── Entities/
│   │   ├── ApplicationEntityTests.cs
│   │   ├── EndpointEntityTests.cs
│   │   ├── MessageEntityTests.cs
│   │   └── ...
│   ├── Enums/
│   ├── Models/
│   ├── Options/
│   └── WebhookEngine.Core.Tests.csproj
├── WebhookEngine.Infrastructure.Tests/
│   ├── Repositories/
│   │   ├── ApplicationRepositoryTests.cs
│   │   ├── EndpointRepositoryTests.cs
│   │   ├── MessageRepositoryTests.cs
│   │   └── ...
│   ├── Services/
│   │   ├── HmacSigningServiceTests.cs
│   │   ├── EndpointHealthTrackerTests.cs
│   │   ├── EndpointRateLimiterTests.cs
│   │   └── ...
│   └── WebhookEngine.Infrastructure.Tests.csproj
└── WebhookEngine.API.Tests/
    ├── Middleware/
    │   └── ApiKeyAuthMiddlewareTests.cs
    └── WebhookEngine.API.Tests.csproj
```

## Test Suite Organization

**Class Structure:**

```csharp
namespace WebhookEngine.Core.Tests.Entities;

public class ApplicationEntityTests
{
    [Fact]
    public void New_Application_Has_Expected_Defaults()
    {
        var app = new Application();

        app.Id.Should().Be(Guid.Empty);
        app.Name.Should().BeEmpty();
        app.IsActive.Should().BeTrue();
    }

    [Fact]
    public void Navigation_Collections_Are_Initialized_Empty()
    {
        var app = new Application();
        app.EventTypes.Should().NotBeNull().And.BeEmpty();
    }

    [Theory]
    [InlineData("whe_abc_randomkey123", "whe", "abc")]
    [InlineData("whe_shortid_abcdef...", "whe", "shortid")]
    public void Api_Key_Prefix_Extraction_Is_Correct(string apiKey, string part0, string part1)
    {
        var parts = apiKey.Split('_');
        parts.Length.Should().BeGreaterThanOrEqualTo(3);
    }
}
```

**Test Naming:**
- MethodName_Scenario_ExpectedResult pattern: `New_Application_Has_Expected_Defaults`
- Descriptive names in snake_case: `Navigation_Collections_Are_Initialized_Empty`
- Clarity over brevity

**Attributes:**
- `[Fact]` for single test cases
- `[Theory]` + `[InlineData(...)]` for parameterized tests with multiple inputs
- `[Theory]` allows multiple related test cases in one method

## Test Patterns

### Unit Test Structure

```csharp
public class HmacSigningServiceTests
{
    private readonly HmacSigningService _sut = new();

    [Fact]
    public void Sign_Returns_Valid_SignedHeaders()
    {
        // Arrange
        var messageId = Guid.NewGuid().ToString();
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var body = """{"event":"order.created","data":{"id":123}}""";
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        // Act
        var result = _sut.Sign(messageId, timestamp, body, secret);

        // Assert
        result.WebhookId.Should().Be(messageId);
        result.WebhookTimestamp.Should().Be(timestamp.ToString());
        result.WebhookSignature.Should().StartWith("v1,");
    }

    [Fact]
    public void Sign_Different_Secret_Produces_Different_Signature()
    {
        var messageId = "msg_abc";
        var timestamp = 1700000000L;
        var body = """{"data":"hello"}""";
        var secret1 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var secret2 = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var result1 = _sut.Sign(messageId, timestamp, body, secret1);
        var result2 = _sut.Sign(messageId, timestamp, body, secret2);

        result1.WebhookSignature.Should().NotBe(result2.WebhookSignature);
    }
}
```

**Patterns:**
- System Under Test (SUT): `private readonly HmacSigningService _sut = new();` instantiated once per class
- Arrange-Act-Assert (AAA) structure: Comments show separation
- FluentAssertions syntax: `.Should().Be()`, `.Should().NotBe()`, `.Should().Contain()`

### Integration Tests with Database

```csharp
public class EndpointRepositoryTests
{
    [Fact]
    public async Task CountByAppIdAsync_Respects_Status_Filter()
    {
        // Arrange
        await using var db = CreateDbContext();
        var repository = new EndpointRepository(db);

        var app = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = "App",
            ApiKeyPrefix = "whe_app_",
            ApiKeyHash = "hash",
            SigningSecret = "secret"
        };

        var active1 = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/1",
            Status = EndpointStatus.Active
        };
        var disabled = new Endpoint
        {
            Id = Guid.NewGuid(),
            AppId = app.Id,
            Url = "https://example.com/3",
            Status = EndpointStatus.Disabled
        };

        db.Applications.Add(app);
        db.Endpoints.AddRange(active1, active2, disabled);
        await db.SaveChangesAsync();

        // Act
        var activeCount = await repository.CountByAppIdAsync(app.Id, EndpointStatus.Active, CancellationToken.None);
        var disabledCount = await repository.CountByAppIdAsync(app.Id, EndpointStatus.Disabled, CancellationToken.None);
        var allCount = await repository.CountByAppIdAsync(app.Id, null, CancellationToken.None);

        // Assert
        activeCount.Should().Be(2);
        disabledCount.Should().Be(1);
        allCount.Should().Be(3);
    }

    private static WebhookDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<WebhookDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new WebhookDbContext(options);
    }
}
```

**Patterns:**
- `await using var db = CreateDbContext();` for in-memory database lifecycle
- `CreateDbContext()` helper uses InMemoryDatabase with unique GUID per test
- Full entity setup with navigation properties
- `CancellationToken.None` passed explicitly

### Parameterized Tests

```csharp
[Theory]
[InlineData("/api/v1/auth/login", true)]
[InlineData("/api/v1/endpoints", false)]
public void Path_Should_Skip_Or_Require_Auth(string path, bool shouldSkipAuth)
{
    var requiresAuth = path.StartsWith("/api/v1/")
        && !path.StartsWith("/api/v1/auth")
        && !path.StartsWith("/api/v1/dashboard");

    if (shouldSkipAuth)
    {
        requiresAuth.Should().BeFalse($"path '{path}' should skip API key auth");
    }
    else
    {
        requiresAuth.Should().BeTrue($"path '{path}' should require API key auth");
    }
}

[Theory]
[InlineData("whe_abc_randomkey123", "whe", "abc", "randomkey123")]
[InlineData("whe_shortid_longstring...", "whe", "shortid", "...")]
public void Api_Key_Prefix_Extraction_Is_Correct(string apiKey, string part0, string part1, string _)
{
    var parts = apiKey.Split('_');
    parts.Length.Should().BeGreaterThanOrEqualTo(3);
}
```

**Patterns:**
- Multiple `[InlineData(...)]` attributes per `[Theory]` method
- Parameters match theory method signature
- Underscore `_` for unused parameters: `public void Test(string param, string _)`
- Condition-based assertions in test body

### Error Testing

```csharp
[Fact]
public void Sign_With_Empty_Secret_Throws_InvalidOperationException()
{
    var act = () => _sut.Sign("msg_1", 123, "{}", "");

    act.Should().Throw<InvalidOperationException>()
        .WithMessage("*secret*missing*");
}

[Fact]
public void Sign_With_Null_Secret_Throws_InvalidOperationException()
{
    var act = () => _sut.Sign("msg_1", 123, "{}", null!);

    act.Should().Throw<InvalidOperationException>()
        .WithMessage("*secret*missing*");
}
```

**Patterns:**
- Arrow function wrapping for Act: `var act = () => _sut.Method(...)`
- Exception assertion: `.Should().Throw<ExceptionType>().WithMessage("pattern")`
- Message matching with wildcards: `"*secret*missing*"`
- `null!` suppression for null-coalescing tests

## Test Infrastructure

**Test Projects:**

| Project | Target | Purpose |
|---------|--------|---------|
| `WebhookEngine.Core.Tests` | net10.0 | Entity, enum, model, option tests — no dependencies |
| `WebhookEngine.Infrastructure.Tests` | net10.0 | Repository and service tests — uses in-memory DB |
| `WebhookEngine.API.Tests` | net10.0 | Middleware, controller, validation tests — uses WebApplicationFactory (NOT YET) |
| `WebhookEngine.Worker.Tests` | net10.0 | Worker logic tests |
| `WebhookEngine.Application.Tests` | net10.0 | Application service tests |

**Project File Setup:**

```xml
<PropertyGroup>
  <TargetFramework>net10.0</TargetFramework>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <IsPackable>false</IsPackable>
</PropertyGroup>

<ItemGroup>
  <PackageReference Include="FluentAssertions" Version="8.8.0" />
  <PackageReference Include="xunit" Version="2.9.3" />
  <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.3.0" />
  <PackageReference Include="coverlet.collector" Version="8.0.0" />
</ItemGroup>

<ItemGroup>
  <Using Include="Xunit" />
</ItemGroup>
```

**Global Usings:**
- Xunit is globally using'd: `<Using Include="Xunit" />`
- Implicit usings enabled: `<ImplicitUsings>enable</ImplicitUsings>`

## Async Testing

**Pattern:**
```csharp
[Fact]
public async Task CountByAppIdAsync_Respects_Status_Filter()
{
    await using var db = CreateDbContext();
    var repository = new EndpointRepository(db);

    var activeCount = await repository.CountByAppIdAsync(app.Id, EndpointStatus.Active, CancellationToken.None);

    activeCount.Should().Be(2);
}
```

**Patterns:**
- Methods marked `async Task` (not `void async`)
- `await` used on all async operations
- `await using` for resource cleanup
- `CancellationToken.None` or `CancellationToken` passed explicitly

## What IS Tested

**Entity/Model Tests:** `ApplicationEntityTests.cs`, `EndpointEntityTests.cs`, `MessageEntityTests.cs`
- Default values and initialization
- Navigation collections
- Property assignments

**Service Tests:** `HmacSigningServiceTests.cs`, `EndpointHealthTrackerTests.cs`, `EndpointRateLimiterTests.cs`
- Signature generation and verification
- Determinism and edge cases
- Error conditions (null, empty, invalid input)
- Different inputs produce different outputs

**Repository Tests:** `ApplicationRepositoryTests.cs`, `EndpointRepositoryTests.cs`, `MessageRepositoryTests.cs`
- CRUD operations with in-memory database
- Query filtering by status, app, entity type
- Pagination logic
- Related entity loading

**Middleware Tests:** `ApiKeyAuthMiddlewareTests.cs`
- Path-based auth requirement logic
- API key format validation
- Bearer token extraction

**Validators:** `RequestValidators.cs` (no explicit tests — validation uses FluentValidation)
- MaximumLength rules
- Email format validation
- Custom validation rules (HTTPS URL)

## What is NOT Tested

**Not covered:**
- Controller integration tests (no WebApplicationFactory tests present)
- End-to-end API flow tests
- Background worker processing logic (DeliveryWorker, RetryScheduler)
- Database migrations
- SignalR hub connections
- HTTP delivery retries and timeouts

**Frontend (Dashboard):**
- No unit tests for React components
- No test files for TypeScript API functions or hooks
- ESLint used for static analysis only

## Coverage Gaps

**High Priority:**
- `WebhookEngine.Worker.DeliveryWorker` — core business logic untested
- `WebhookEngine.API.Controllers` — no integration tests, only middleware tests
- Dashboard components — no tests at all
- Error retry scenarios

**Medium Priority:**
- SignalR notification delivery
- Rate limiting edge cases
- Circuit breaker state transitions

---

*Testing analysis: 2026-03-30*
