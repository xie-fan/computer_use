using ComputerUse.Mcp.Capture;

namespace ComputerUse.Mcp.Tests;

public sealed class TtlLruCacheTests
{
    [Fact]
    public void OverCapacity_EvictsLeastRecentlyUsed()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var cache = new TtlLruCache<string, Box>(2, TimeSpan.FromMinutes(1), () => now);

        cache.Put("a", new Box("a"));
        cache.Put("b", new Box("b"));
        Assert.True(cache.TryGet("a", out _));

        var evicted = cache.Put("c", new Box("c"));
        Assert.Equal(["b"], evicted.Select(x => x.Name));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("a", out var a));
        Assert.Equal("a", a.Name);
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void ExpiredEntry_IsEvictedAndNotReturned()
    {
        var now = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var cache = new TtlLruCache<string, Box>(2, TimeSpan.FromSeconds(3), () => now);
        cache.Put("a", new Box("a"));

        now = now.AddSeconds(4);
        var expired = cache.EvictExpired();
        Assert.Equal(["a"], expired.Select(x => x.Name));
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void PutSameKey_ReturnsPreviousValue()
    {
        var cache = new TtlLruCache<string, Box>(2, TimeSpan.FromMinutes(1));
        cache.Put("a", new Box("old"));
        var evicted = cache.Put("a", new Box("new"));
        Assert.Equal(["old"], evicted.Select(x => x.Name));
        Assert.True(cache.TryGet("a", out var current));
        Assert.Equal("new", current.Name);
    }

    private sealed class Box(string name)
    {
        public string Name { get; } = name;
    }
}
