using Microsoft.AspNetCore.SignalR;
using System.Threading.RateLimiting;

namespace Uno.Api.Hubs;

public sealed class UnoHubThrottle : IHubFilter
{
    private static readonly object LimiterKey = new();
    private static readonly object SharedLock = new();
    private static readonly Dictionary<string,(double Tokens,long Updated)> Shared = new();
    private static bool AcquireShared(string key)
    {
        lock (SharedLock)
        {
            var now = Environment.TickCount64;
            if (Shared.Count >= 10000)
            {
                foreach (var expired in Shared.Where(x => now-x.Value.Updated>60000).Select(x=>x.Key).ToArray())
                    Shared.Remove(expired);
                if (Shared.Count>=10000 && !Shared.ContainsKey(key)) return false;
            }
            var previous = Shared.GetValueOrDefault(key,(Tokens:40d,Updated:now));
            var tokens = Math.Min(40,previous.Tokens+(now-previous.Updated)*0.02);
            Shared[key]=(Math.Max(0,tokens-1),now);
            return tokens>=1;
        }
    }
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext context, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        var ip = context.Context.GetHttpContext()?.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        if (!AcquireShared("ip:"+ip)) throw new HubException("Too many requests. Try again shortly.");
        if (context.HubMethodArguments.Count>=2 && context.HubMethodArguments[1] is string token && token.Length<=128 &&
            !AcquireShared("seat:"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token)))))
            throw new HubException("Too many requests. Try again shortly.");
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
