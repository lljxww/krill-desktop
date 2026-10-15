using System.Text.Json;
using System.Text.Json.Serialization;

namespace Krill.Core;

public static class KrillJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };
}

public sealed class ApiEnvelope<T>
{
    public bool Success { get; set; }
    public int Code { get; set; }
    public T? Data { get; set; }
}

public sealed class UserProfile
{
    public JsonElement Id { get; set; }
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? Name { get; set; }
    public string? Nickname { get; set; }
    public string? DisplayName { get; set; }

    [JsonIgnore]
    public string FriendlyName => new[] { DisplayName, Nickname, Name, Username, Email?.Split('@')[0] }
        .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "Krill 用户";
}

public sealed class SubscriptionResponse
{
    public List<Subscription> Subscriptions { get; set; } = [];
    public SubscriptionSummary? Summary { get; set; }
    public decimal? CreditBalanceUsd { get; set; }
    public decimal? WelfareBalanceUsd { get; set; }
}

public sealed class SubscriptionSummary
{
    public decimal? TotalDailyQuotaUsd { get; set; }
    public decimal? TotalUsedUsd { get; set; }
    public decimal? TotalRemainingUsd { get; set; }
    public decimal? TotalForwardedRemainingUsd { get; set; }
    public DateTimeOffset? SubscriptionEndAt { get; set; }
}

public sealed class Subscription
{
    public long SubscriptionId { get; set; }
    public string? Status { get; set; }
    public Plan Plan { get; set; } = new();
    public Quota Quota { get; set; } = new();
    public DateTimeOffset? SubscriptionStartAt { get; set; }
    public DateTimeOffset? SubscriptionEndAt { get; set; }
    public decimal? TotalUsedUsd { get; set; }
    public decimal? TotalLimitUsd { get; set; }
    public decimal? TotalRemainingUsd { get; set; }
    public int ConsumePriority { get; set; }
}

public sealed class Plan
{
    public long Id { get; set; }
    public string Name { get; set; } = "Krill 套餐";
    public string? BillingType { get; set; }
    public decimal? DailyQuotaUsd { get; set; }
    public int DurationDays { get; set; }
    public decimal? TotalCredits { get; set; }
}

public sealed class Quota
{
    public string? Date { get; set; }
    public decimal? DailyLimitUsd { get; set; }
    public decimal? UsedUsd { get; set; }
    public decimal? RemainingUsd { get; set; }
    public decimal? ForwardedLimitUsd { get; set; }
    public decimal? ForwardedUsedUsd { get; set; }
    public decimal? ForwardedRemainingUsd { get; set; }
    public DateTimeOffset? WindowStartAt { get; set; }
    public DateTimeOffset? WindowResetAt { get; set; }
    public decimal? LimitCredits { get; set; }
    public decimal? UsedCredits { get; set; }
    public decimal? RemainingCredits { get; set; }
    public long? LimitRequests { get; set; }
    public long? UsedRequests { get; set; }
    public long? RemainingRequests { get; set; }
}

public sealed class RequestStats
{
    public long TotalRequests { get; set; }
    public long SuccessRequests { get; set; }
    public long FailedRequests { get; set; }
    public long TotalTokens { get; set; }
    public long InputTokens { get; set; }
    public long OutputTokens { get; set; }
    public decimal? TotalCostUsd { get; set; }
    public List<TrendBucket> Trend { get; set; } = [];
}

public sealed class TrendBucket
{
    public DateTimeOffset BucketStart { get; set; }
    public long RequestCount { get; set; }
    public long TotalTokens { get; set; }
    public decimal? TotalCostUsd { get; set; }
}

public sealed class AnnouncementsResponse
{
    public List<Announcement> Items { get; set; } = [];
}

public sealed class Announcement
{
    public long Id { get; set; }
    public string Title { get; set; } = "公告";
    public string? Summary { get; set; }
    public string? Content { get; set; }
    public DateTimeOffset? CreatedAt { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
}
