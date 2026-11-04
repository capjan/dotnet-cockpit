using Xunit;

namespace DotnetCockpit.Tests;

public sealed class LiveDataTests
{
    private sealed class Counter
    {
        private int value;
        public int Value => Volatile.Read(ref value);
        public async Task<string> LoadAsync()
        {
            await Task.Yield();
            return $"value {Interlocked.Increment(ref value)}";
        }
    }

    [Fact]
    public async Task AnswersFromMemoryAndRefreshesInTheBackgroundOnceAged()
    {
        var counter = new Counter();
        var cache = new StaleWhileRevalidate<string>(counter.LoadAsync, TimeSpan.Zero);

        Assert.Equal("value 1", await cache.GetAsync());
        await Task.Delay(5);
        var second = cache.GetAsync();

        Assert.True(second.IsCompletedSuccessfully);
        Assert.Equal("value 1", await second);
        await Task.Delay(50);
        Assert.Equal("value 2", cache.Current);
    }

    [Fact]
    public async Task RefreshReturnsAValueReadAfterTheCall()
    {
        var counter = new Counter();
        var cache = new StaleWhileRevalidate<string>(counter.LoadAsync, TimeSpan.FromHours(1));
        await cache.GetAsync();

        Assert.Equal("value 2", await cache.RefreshAsync());
        Assert.Equal("value 2", await cache.GetAsync());
    }

    [Fact]
    public async Task ConcurrentReadersShareOneLoad()
    {
        var counter = new Counter();
        var cache = new StaleWhileRevalidate<string>(counter.LoadAsync, TimeSpan.FromHours(1));

        await Task.WhenAll(cache.GetAsync(), cache.GetAsync(), cache.GetFreshAsync(TimeSpan.FromHours(1)));

        Assert.Equal(1, counter.Value);
    }

    [Fact]
    public async Task AFailedReadIsNotKept()
    {
        var counter = new Counter();
        var cache = new StaleWhileRevalidate<string>(counter.LoadAsync, TimeSpan.FromHours(1), value => value != "value 1");

        Assert.Equal("value 1", await cache.GetAsync());
        Assert.Null(cache.Current);
        Assert.Equal("value 2", await cache.GetAsync());
        Assert.Equal("value 2", cache.Current);
    }
}
