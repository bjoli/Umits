using Collections;
using NUnit.Framework;

namespace rrbtests;

[TestFixture]
public class RrbListHigherOrderTests
{
    private RrbList<int> CreateList(params int[] items)
    {
        return new RrbList<int>().AddRange(items);
    }

    [Test]
    public void Filter_KeepsMatchingElements()
    {
        var list = CreateList(1, 2, 3, 4, 5, 6);
        var filtered = list.Filter(x => x % 2 == 0);

        Assert.That(filtered, Is.EquivalentTo(new[] { 2, 4, 6 }));
        Assert.That(filtered.Count, Is.EqualTo(3));
    }

    [Test]
    public void Filter_WithEmptyList_ReturnsEmpty()
    {
        var list = RrbList<int>.Empty;
        var filtered = list.Filter(x => x > 0);

        Assert.That(filtered, Is.Empty);
    }

    [Test]
    public void Filter_NoMatches_ReturnsEmptyList()
    {
        var list = CreateList(1, 3, 5);
        var filtered = list.Filter(x => x % 2 == 0);

        Assert.That(filtered, Is.Empty);
    }

    [Test]
    public void Map_TransformsElements()
    {
        var list = CreateList(1, 2, 3);
        var mapped = list.Map(x => x * 10);

        Assert.That(mapped, Is.EquivalentTo(new[] { 10, 20, 30 }));
        Assert.That(mapped.Count, Is.EqualTo(3));
    }

    [Test]
    public void Map_WithEmptyList_ReturnsEmpty()
    {
        var list = RrbList<int>.Empty;
        var mapped = list.Map(x => x.ToString());

        Assert.That(mapped, Is.Empty);
    }

    [Test]
    public void Map_ChangesType()
    {
        var list = CreateList(1, 2, 3);
        var mapped = list.Map(x => $"Number {x}");

        Assert.That(mapped, Is.EquivalentTo(new[] { "Number 1", "Number 2", "Number 3" }));
        Assert.That(mapped.Count, Is.EqualTo(3));
    }

    [Test]
    public void Map_LargeList_TransformsElements()
    {
        var items = Enumerable.Range(0, 1000).ToArray();
        var list = new RrbList<int>().AddRange(items);
        
        var mapped = list.Map(x => x + 1);
        
        var expected = items.Select(x => x + 1).ToArray();
        Assert.That(mapped, Is.EquivalentTo(expected));
    }

    [Test]
    public void Filter_LargeList_KeepsMatchingElements()
    {
        var items = Enumerable.Range(0, 1000).ToArray();
        var list = new RrbList<int>().AddRange(items);
        
        var filtered = list.Filter(x => x % 3 == 0);
        
        var expected = items.Where(x => x % 3 == 0).ToArray();
        Assert.That(filtered, Is.EquivalentTo(expected));
    }
}