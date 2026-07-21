using VisualCat.Core.Store;

namespace VisualCat.Core.Tests;

public sealed class RankBitmapTests
{
    [Fact]
    public void RankAndBooleanOperationsMatchNaiveOracle()
    {
        var random = new Random(4242);
        for (var iteration = 0; iteration < 100; iteration++)
        {
            var length = random.Next(0, 2048);
            var leftValues = Enumerable.Range(0, length).Select(_ => random.Next(3) == 0).ToArray();
            var rightValues = Enumerable.Range(0, length).Select(_ => random.Next(4) == 0).ToArray();
            var left = RankBitmap.FromPredicate(length, index => leftValues[index]);
            var right = RankBitmap.FromPredicate(length, index => rightValues[index]);
            for (var end = 0; end <= length; end += Math.Max(1, length / 17))
            {
                Assert.Equal(leftValues.Take(end).Count(static value => value), left.Rank(end));
            }

            var and = left.And(right);
            var or = left.Or(right);
            var andNot = left.AndNot(right);
            for (var index = 0; index < length; index++)
            {
                Assert.Equal(leftValues[index] && rightValues[index], and[index]);
                Assert.Equal(leftValues[index] || rightValues[index], or[index]);
                Assert.Equal(leftValues[index] && !rightValues[index], andNot[index]);
            }
        }
    }

    [Fact]
    public void PersistenceRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bitmap-{Guid.NewGuid():N}.rbm");
        try
        {
            var bitmap = RankBitmap.FromPredicate(513, index => index % 7 == 0);
            bitmap.Save(path);
            var loaded = RankBitmap.Load(path);
            Assert.Equal(bitmap.Length, loaded.Length);
            Assert.Equal(bitmap.Cardinality, loaded.Cardinality);
            Assert.Equal(bitmap.Words.ToArray(), loaded.Words.ToArray());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
