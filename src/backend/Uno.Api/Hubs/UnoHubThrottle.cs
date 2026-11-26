using Microsoft.AspNetCore.SignalR;
using System.Threading.RateLimiting;

namespace Uno.Api.Hubs;

public sealed class UnoHubThrottle : IHubFilter
{
    private static readonly object LimiterKey = new();
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        if (!context.Context.Items.TryGetValue(LimiterKey, out var value))
        {
            value = new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
            {
                TokenLimit = 20, TokensPerPeriod = 10, ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                AutoReplenishment = false, QueueLimit = 0
            });
            context.Context.Items[LimiterKey] = value;
        }
        var limiter = (TokenBucketRateLimiter)value!;
        limiter.TryReplenish();
        using var lease = limiter.AttemptAcquire();
        if (!lease.IsAcquired) throw new HubException("Too many requests. Try again shortly.");
        return await next(context);
    }
    public async Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        try { await next(context, exception); }
        finally
        {
            if (context.Context.Items.Remove(LimiterKey, out var value) && value is IDisposable disposable)
                disposable.Dispose();
        }
    }
}
