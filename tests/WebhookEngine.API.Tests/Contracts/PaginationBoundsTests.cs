using FluentAssertions;
using WebhookEngine.API.Contracts;

namespace WebhookEngine.API.Tests.Contracts;

// A10: shared clamp is reused across all 11 list actions, so this unit proof plus
// one API integration proof (MessagesListInputBoundsTests) cover the whole surface.
public class PaginationBoundsTests
{
    [Theory]
    [InlineData(0, 20, 1, 20)]
    [InlineData(-5, 20, 1, 20)]
    [InlineData(3, 20, 3, 20)]
    [InlineData(1, 0, 1, 1)]
    [InlineData(1, -1, 1, 1)]
    [InlineData(1, 2_000_000_000, 1, 100)]
    [InlineData(1, 50, 1, 50)]
    [InlineData(1, 100, 1, 100)]
    public void Clamp_Floors_Page_At_1_And_Bounds_PageSize_To_Max(
        int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var (clampedPage, clampedPageSize) = PaginationBounds.Clamp(page, pageSize);

        clampedPage.Should().Be(expectedPage);
        clampedPageSize.Should().Be(expectedPageSize);
    }

    [Fact]
    public void MaxPageSize_Is_100()
    {
        PaginationBounds.MaxPageSize.Should().Be(100);
    }

    [Fact]
    public void Pagination_When_PageSize_Is_Zero_Does_Not_Divide_By_Zero_And_Yields_Zero_TotalPages()
    {
        // Guards the case where a caller reaches Pagination without clamping first.
        var act = () => ApiEnvelope.Pagination(page: 1, pageSize: 0, totalCount: 45);

        act.Should().NotThrow();

        var meta = ApiEnvelope.Pagination(page: 1, pageSize: 0, totalCount: 45);
        meta.TotalPages.Should().Be(0);
        meta.HasNext.Should().BeFalse();
        meta.HasPrev.Should().BeFalse();
    }

    [Fact]
    public void Pagination_For_A_Middle_Page_Computes_TotalPages_And_Nav_Flags()
    {
        var meta = ApiEnvelope.Pagination(page: 2, pageSize: 20, totalCount: 45);

        meta.Page.Should().Be(2);
        meta.PageSize.Should().Be(20);
        meta.TotalCount.Should().Be(45);
        meta.TotalPages.Should().Be(3, "ceil(45 / 20) = 3");
        meta.HasNext.Should().BeTrue("page 2 of 3 has a next page");
        meta.HasPrev.Should().BeTrue("page 2 has a previous page");
    }
}
