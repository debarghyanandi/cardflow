using Cardflow.Api.Ordering;
using Xunit;

namespace Cardflow.Api.Tests.Ordering;

public sealed class FractionalRankTests
{
    [Fact]
    public void GeneratesRanksAtStartMiddleAndEnd()
    {
        var first = FractionalRank.Between(null, null);
        var atStart = FractionalRank.Between(null, first);
        var atEnd = FractionalRank.Between(first, null);
        var inMiddle = FractionalRank.Between(first, atEnd);

        Assert.Equal("1", first);
        Assert.True(string.CompareOrdinal(atStart, first) < 0);
        Assert.True(string.CompareOrdinal(first, inMiddle) < 0);
        Assert.True(string.CompareOrdinal(inMiddle, atEnd) < 0);
    }

    [Fact]
    public void CanKeepInsertingInTheSameGap()
    {
        var left = "01";
        const string right = "1";

        for (var index = 0; index < 100; index++)
        {
            var next = FractionalRank.Between(left, right);
            Assert.True(string.CompareOrdinal(left, next) < 0);
            Assert.True(string.CompareOrdinal(next, right) < 0);
            left = next;
        }
    }

    [Fact]
    public void RejectsInvalidOrReversedBounds()
    {
        Assert.Throws<ArgumentException>(() => FractionalRank.Between("1", "01"));
        Assert.Throws<ArgumentException>(() => FractionalRank.Between("10", null));
    }

    [Fact]
    public void SpreadProducesShortOrderedRanks()
    {
        var ranks = FractionalRank.Spread(200);
        Assert.Equal(200, ranks.Distinct().Count());
        Assert.All(ranks, rank => Assert.True(rank.Length < 50));
        Assert.Equal(ranks, ranks.OrderBy(rank => rank, StringComparer.Ordinal));
    }
}
