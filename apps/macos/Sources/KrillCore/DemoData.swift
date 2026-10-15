import Foundation

public enum DemoData {
    public static var profile: UserProfile { UserProfile(email: "explorer@example.com", name: "Krill Explorer") }
    public static func subscriptions(now: Date = Date()) -> SubscriptionResponse {
        var weekly = Quota(); weekly.dailyLimitUsd = 100; weekly.usedUsd = Decimal(string: "27.40")
        weekly.remainingUsd = Decimal(string: "72.60"); weekly.forwardedRemainingUsd = Decimal(string: "8.20")
        weekly.windowResetAt = now.addingTimeInterval(4 * 86400)
        var daily = Quota(); daily.dailyLimitUsd = 20; daily.usedUsd = Decimal(string: "3.20")
        daily.remainingUsd = Decimal(string: "16.80"); daily.windowResetAt = now.addingTimeInterval(6 * 3600)
        return SubscriptionResponse(subscriptions: [
            Subscription(id: 1, status: "active", plan: Plan(name: "Codex Pro · 周套餐", billingType: "usd_weekly"), quota: weekly, end: now.addingTimeInterval(23 * 86400), priority: 1),
            Subscription(id: 2, status: "active", plan: Plan(name: "Claude · 日套餐", billingType: "usd_daily"), quota: daily, end: now.addingTimeInterval(12 * 86400), priority: 2)
        ], creditBalanceUsd: Decimal(string: "24.80"), welfareBalanceUsd: Decimal(string: "3.50"))
    }
    public static func stats(now: Date = Date()) -> RequestStats {
        let start = Calendar.current.startOfDay(for: now)
        let trend: [TrendBucket] = (0..<24).map { (hour: Int) -> TrendBucket in
            let requests = Int64((hour * 7 + 3) % 19)
            let cost = Decimal((hour * 13 + 5) % 29) / Decimal(10)
            return TrendBucket(start: start.addingTimeInterval(Double(hour) * 3600), requests: requests, cost: cost)
        }
        return RequestStats(requests: 128, successes: 126, tokens: 1_284_600, input: 1_046_200, output: 238_400,
                            cost: Decimal(string: "7.42")!, trend: trend)
    }
    public static func announcements(now: Date = Date()) -> [Announcement] {
        [Announcement(id: 3, title: "欢迎使用 Krill Desktop", summary: "额度、余额与套餐，一眼就能看见。此处为虚构演示内容。", date: now.addingTimeInterval(-7200)),
         Announcement(id: 2, title: "让桌面留一点空间给重要的事", summary: "菜单栏进度环随所选套餐更新。点击图标即可查看详情。", date: now.addingTimeInterval(-86400)),
         Announcement(id: 1, title: "你的数据，仅保存在本机", summary: "会话存入 macOS 钥匙串；演示模式不会请求个人账号接口。", date: now.addingTimeInterval(-3 * 86400))]
    }
}
