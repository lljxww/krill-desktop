namespace Krill.Core;

// Synthetic fixtures, deliberately independent of personal HAR values and credentials.
public static class DemoData
{
    public static UserProfile Profile => new() { Name = "Krill Explorer", Email = "explorer@example.com" };
    public static SubscriptionResponse Subscriptions(DateTimeOffset now) => new()
    {
        CreditBalanceUsd = 24.80m, WelfareBalanceUsd = 3.50m,
        Subscriptions =
        [
            new() { SubscriptionId = 1, Status = "active", ConsumePriority = 1,
                Plan = new() { Name = "Codex Pro · 周套餐", BillingType = "usd_weekly", DurationDays = 30 },
                SubscriptionEndAt = now.AddDays(23),
                Quota = new() { DailyLimitUsd = 100m, UsedUsd = 27.40m, RemainingUsd = 72.60m,
                    ForwardedRemainingUsd = 8.20m, WindowStartAt = now.AddDays(-3), WindowResetAt = now.AddDays(4) } },
            new() { SubscriptionId = 2, Status = "active", ConsumePriority = 2,
                Plan = new() { Name = "Claude · 日套餐", BillingType = "usd_daily", DurationDays = 30 },
                SubscriptionEndAt = now.AddDays(12),
                Quota = new() { DailyLimitUsd = 20m, UsedUsd = 3.20m, RemainingUsd = 16.80m,
                    ForwardedRemainingUsd = 0m, WindowResetAt = now.AddHours(6) } }
        ]
    };
    public static RequestStats Stats(DateTimeOffset now) => new()
    {
        TotalRequests = 128, SuccessRequests = 126, FailedRequests = 2, TotalTokens = 1_284_600,
        InputTokens = 1_046_200, OutputTokens = 238_400, TotalCostUsd = 7.42m,
        Trend = Enumerable.Range(0, 24).Select(i => new TrendBucket
        { BucketStart = now.Date.AddHours(i), RequestCount = (i * 7 + 3) % 19,
            TotalCostUsd = ((i * 13 + 5) % 29) / 10m }).ToList()
    };
    public static AnnouncementsResponse Announcements(DateTimeOffset now) => new()
    {
        Items =
        [
            new() { Id = 3, Title = "欢迎使用 Krill Desktop", Summary = "额度、余额与套餐，一眼就能看见。此处为演示内容，连接账号后将显示官网公告。", CreatedAt = now.AddHours(-2) },
            new() { Id = 2, Title = "让桌面留一点空间给重要的事", Summary = "拖动悬浮球调整位置，单击展开面板；在托盘中也可以随时找回它。", CreatedAt = now.AddDays(-1) },
            new() { Id = 1, Title = "你的数据，仅保存在本机", Summary = "登录会话保存在当前 Windows 用户下。演示模式不会连接个人账号。", CreatedAt = now.AddDays(-3) }
        ]
    };
}
