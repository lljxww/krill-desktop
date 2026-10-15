using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Krill.Core;
using Krill.Desktop.Services;

namespace Krill.Desktop.ViewModels;

public sealed record DashboardActions(Func<Task> Login, Func<Task> Refresh, Action<string> OpenWebsite,
    Action SignOut, Action Exit, Action PreferencesChanged, Action<long> MarkAnnouncementRead, Action ShowDemo);

public sealed record SubscriptionCard(Subscription Source, QuotaProjection Quota, bool IsActive)
{
    public long Id => Source.SubscriptionId;
    public string Name => Source.Plan.Name;
    public string Cycle => Quota.Cycle;
    public string Remaining => DisplayFormat.Amount(Quota, Quota.Remaining);
    public string Limit => DisplayFormat.Amount(Quota, Quota.Limit);
    public string Used => DisplayFormat.Amount(Quota, Quota.Used);
    public string Forwarded => DisplayFormat.Money(Quota.Forwarded);
    public string Reset => Quota.ResetAt.HasValue ? $"{DisplayFormat.LocalDate(Quota.ResetAt)} 重置" : "周期时间以官网为准";
    public string Expiry => $"{DisplayFormat.LocalDate(Source.SubscriptionEndAt)} 到期";
    public string State => IsActive ? "使用中" : Source.Status == "frozen" ? "已冻结" : "已结束 / 不可用";
    public double Percent => Quota.Percent ?? 0;
    public bool HasForwarded => Quota.Forwarded > 0;
}

public sealed record AnnouncementCard(long Id, string Title, string Summary, string Date, bool IsUnread);
public sealed record ChartBar(double Height, string Tooltip);

public partial class DashboardViewModel : ObservableObject
{
    private readonly DashboardActions _actions;
    private readonly ClientSettings _settings;
    private bool _applying;
    private SubscriptionResponse? _subscriptions;
    private RequestStats? _stats;

    public DashboardViewModel(ClientSettings settings, DashboardActions actions)
    {
        _settings = settings; _actions = actions;
        alwaysOnTop = settings.AlwaysOnTop; snapToEdge = settings.SnapToEdge;
        panelPinned = settings.PanelPinned; startWithWindows = settings.StartWithWindows;
        refreshSeconds = settings.RefreshSeconds;
        LoginCommand = new AsyncRelayCommand(actions.Login);
        RefreshCommand = new AsyncRelayCommand(actions.Refresh);
        OpenWebsiteCommand = new RelayCommand<string>(path => actions.OpenWebsite(path ?? "/app"));
        SignOutCommand = new RelayCommand(actions.SignOut);
        ExitCommand = new RelayCommand(actions.Exit);
        ShowDemoCommand = new RelayCommand(actions.ShowDemo);
        OpenAnnouncementCommand = new RelayCommand<AnnouncementCard>(item =>
        { if (item is not null) actions.MarkAnnouncementRead(item.Id); actions.OpenWebsite("/app/announcements"); });
    }

    public IAsyncRelayCommand LoginCommand { get; }
    public IAsyncRelayCommand RefreshCommand { get; }
    public IRelayCommand<string> OpenWebsiteCommand { get; }
    public IRelayCommand SignOutCommand { get; }
    public IRelayCommand ExitCommand { get; }
    public IRelayCommand ShowDemoCommand { get; }
    public IRelayCommand<AnnouncementCard> OpenAnnouncementCommand { get; }
    public ObservableCollection<SubscriptionCard> Plans { get; } = [];
    public ObservableCollection<SubscriptionCard> SelectablePlans { get; } = [];
    public ObservableCollection<AnnouncementCard> Announcements { get; } = [];
    public ObservableCollection<AnnouncementCard> RecentAnnouncements { get; } = [];
    public ObservableCollection<ChartBar> ChartBars { get; } = [];
    public int[] RefreshIntervals { get; } = [30, 60, 120, 300];

    [ObservableProperty] private int selectedTab;
    [ObservableProperty] private bool isLoading;
    [ObservableProperty] private bool isDemo;
    [ObservableProperty] private bool hasData;
    [ObservableProperty] private bool hasStats;
    [ObservableProperty] private bool needsLogin = true;
    [ObservableProperty] private bool isStale;
    [ObservableProperty] private bool isLoginOpen;
    [ObservableProperty] private string userName = "你的桌面用量助手";
    [ObservableProperty] private string userEmail = "连接账号，随时掌握剩余额度";
    [ObservableProperty] private string avatarText = "K";
    [ObservableProperty] private string statusTitle = "尚未连接";
    [ObservableProperty] private string statusDetail = "使用网站现有的 GitHub 登录连接账号。";
    [ObservableProperty] private string lastUpdatedText = "等待首次同步";
    [ObservableProperty] private string creditText = "—";
    [ObservableProperty] private string welfareText = "—";
    [ObservableProperty] private string primaryPlanName = "尚未选择套餐";
    [ObservableProperty] private string primaryCycle = "当前周期额度";
    [ObservableProperty] private string primaryRemaining = "—";
    [ObservableProperty] private string primaryLimit = "—";
    [ObservableProperty] private string primaryUsed = "—";
    [ObservableProperty] private string primaryReset = "周期以服务端时间为准";
    [ObservableProperty] private string primaryForwarded = "—";
    [ObservableProperty] private bool hasForwarded;
    [ObservableProperty] private double quotaPercent;
    [ObservableProperty] private string quotaPercentText = "—";
    [ObservableProperty] private string orbText = "登录";
    [ObservableProperty] private string orbCaption = "KRILL";
    [ObservableProperty] private string accentColor = "#7CE3CD";
    [ObservableProperty] private string planCountText = "全部套餐";
    [ObservableProperty] private string unreadText = "最新动态";
    [ObservableProperty] private string requestsText = "—";
    [ObservableProperty] private string tokensText = "—";
    [ObservableProperty] private string costText = "—";
    [ObservableProperty] private string successRateText = "—";
    [ObservableProperty] private string inputTokensText = "—";
    [ObservableProperty] private string outputTokensText = "—";
    [ObservableProperty] private string statsNote = "打开面板后自动加载今日统计。";
    [ObservableProperty] private string announcementNote = "连接后加载官网最新公告。";
    [ObservableProperty] private SubscriptionCard? selectedPlan;
    [ObservableProperty] private bool alwaysOnTop;
    [ObservableProperty] private bool snapToEdge;
    [ObservableProperty] private bool panelPinned;
    [ObservableProperty] private bool startWithWindows;
    [ObservableProperty] private int refreshSeconds;

    public string OrbTooltip => $"Krill Desktop\n{PrimaryPlanName}\n{PrimaryCycle}：{PrimaryRemaining}\n{PrimaryReset}\n{LastUpdatedText}";
    public bool ShowWelcome => !HasData && SelectedTab != 4;

    partial void OnSelectedTabChanged(int value) => OnPropertyChanged(nameof(ShowWelcome));
    partial void OnHasDataChanged(bool value) => OnPropertyChanged(nameof(ShowWelcome));

    partial void OnSelectedPlanChanged(SubscriptionCard? value)
    {
        ProjectQuota();
        if (!_applying) { _settings.SelectedSubscriptionId = value?.Id; _actions.PreferencesChanged(); }
    }
    partial void OnAlwaysOnTopChanged(bool value) => _actions.PreferencesChanged();
    partial void OnSnapToEdgeChanged(bool value) => _actions.PreferencesChanged();
    partial void OnPanelPinnedChanged(bool value) => _actions.PreferencesChanged();
    partial void OnStartWithWindowsChanged(bool value) => _actions.PreferencesChanged();
    partial void OnRefreshSecondsChanged(int value) => _actions.PreferencesChanged();
    partial void OnIsStaleChanged(bool value) => ProjectQuota();
    partial void OnNeedsLoginChanged(bool value) => ProjectQuota();

    public void ApplyCore(UserProfile profile, SubscriptionResponse response, bool demo = false)
    {
        IsDemo = demo; NeedsLogin = false; IsStale = false; HasData = true;
        UserName = profile.FriendlyName;
        UserEmail = profile.Email ?? "已连接 Krill 账号";
        AvatarText = string.IsNullOrEmpty(UserName) ? "K" : StringInfo.GetNextTextElement(UserName).ToUpperInvariant();
        _subscriptions = response;
        CreditText = DisplayFormat.Money(response.CreditBalanceUsd);
        WelfareText = DisplayFormat.Money(response.WelfareBalanceUsd);
        _applying = true;
        var chosen = SelectedPlan?.Id ?? _settings.SelectedSubscriptionId;
        Plans.Clear(); SelectablePlans.Clear();
        foreach (var subscription in (response.Subscriptions ?? []).OrderBy(x => x.ConsumePriority))
        {
            var active = subscription.Status == "active" && (subscription.SubscriptionEndAt is null || subscription.SubscriptionEndAt > DateTimeOffset.UtcNow);
            var card = new SubscriptionCard(subscription, QuotaProjection.From(subscription), active);
            Plans.Add(card); if (active) SelectablePlans.Add(card);
        }
        SelectedPlan = SelectablePlans.FirstOrDefault(x => x.Id == chosen) ?? SelectablePlans.FirstOrDefault();
        _applying = false;
        PlanCountText = $"{SelectablePlans.Count} 个有效套餐";
        LastUpdatedText = $"更新于 {DateTime.Now:HH:mm:ss}";
        StatusTitle = demo ? "演示模式" : "已连接";
        StatusDetail = demo ? "以下为虚构样例数据，不代表真实账号。" : "账号数据已同步，后台会自动更新。";
        ProjectQuota();
    }

    private void ProjectQuota()
    {
        var q = SelectedPlan?.Quota;
        PrimaryPlanName = SelectedPlan?.Name ?? (HasData ? "按余额使用" : "尚未连接账号");
        PrimaryCycle = q?.Cycle ?? "充值余额";
        PrimaryRemaining = q is null ? CreditText : DisplayFormat.Amount(q, q.Remaining);
        PrimaryLimit = q is null ? "—" : DisplayFormat.Amount(q, q.Limit);
        PrimaryUsed = q is null ? "—" : DisplayFormat.Amount(q, q.Used);
        PrimaryReset = SelectedPlan?.Reset ?? "余额按实际使用扣除";
        PrimaryForwarded = DisplayFormat.Money(q?.Forwarded);
        HasForwarded = q?.Forwarded > 0;
        QuotaPercent = q?.Percent ?? 0;
        QuotaPercentText = q?.Percent is double p ? $"{p:0}%" : "—";
        OrbText = NeedsLogin ? "登录" : q?.Percent is double percent ? $"{percent:0}%"
            : _subscriptions?.CreditBalanceUsd is decimal balance ? "$" + balance.ToString("0.##", CultureInfo.InvariantCulture) : "—";
        OrbCaption = IsStale ? "离线" : NeedsLogin ? "KRILL" : q is null ? "余额" : "剩余";
        AccentColor = IsStale || NeedsLogin ? "#8296A3" : q?.Percent < 20 ? "#F3BC73" : "#7CE3CD";
        OnPropertyChanged(nameof(OrbTooltip));
    }

    public void ApplyStats(RequestStats stats)
    {
        _stats = stats; HasStats = true;
        RequestsText = DisplayFormat.Count(stats.TotalRequests);
        TokensText = DisplayFormat.Compact(stats.TotalTokens);
        CostText = DisplayFormat.Cost(stats.TotalCostUsd);
        SuccessRateText = stats.TotalRequests > 0 ? $"{(double)stats.SuccessRequests / stats.TotalRequests * 100:0.#}%" : "—";
        InputTokensText = DisplayFormat.Compact(stats.InputTokens);
        OutputTokensText = DisplayFormat.Compact(stats.OutputTokens);
        StatsNote = $"今日 · 本机时区 · 更新于 {DateTime.Now:HH:mm}";
        ChartBars.Clear();
        var buckets = (stats.Trend ?? []).TakeLast(48).ToArray();
        var max = buckets.Select(b => b.TotalCostUsd ?? 0).DefaultIfEmpty(0).Max();
        foreach (var bucket in buckets)
            ChartBars.Add(new ChartBar(max > 0 ? Math.Max(3, (double)((bucket.TotalCostUsd ?? 0) / max) * 88) : 3,
                $"{bucket.BucketStart.ToLocalTime():HH:mm} · {DisplayFormat.Cost(bucket.TotalCostUsd)} · {bucket.RequestCount} 次请求"));
    }

    public void ApplyAnnouncements(AnnouncementsResponse response)
    {
        Announcements.Clear(); RecentAnnouncements.Clear();
        foreach (var item in (response.Items ?? []).OrderByDescending(x => x.CreatedAt).Take(50))
            Announcements.Add(new(item.Id, DisplayFormat.PlainText(item.Title, 80), DisplayFormat.PlainText(item.Summary ?? item.Content, 180),
                DisplayFormat.LocalDate(item.CreatedAt), item.Id > _settings.LastReadAnnouncementId));
        foreach (var item in Announcements.Take(2)) RecentAnnouncements.Add(item);
        var count = Announcements.Count(x => x.IsUnread);
        UnreadText = count > 0 ? $"{count} 条未读" : "全部已读";
        AnnouncementNote = Announcements.Count > 0 ? "官网最新公告 · 点击前往网站查看完整内容" : "当前没有可显示的公告。";
    }

    public void MarkRead(long id)
    {
        _settings.LastReadAnnouncementId = Math.Max(_settings.LastReadAnnouncementId, id);
        for (var i = 0; i < Announcements.Count; i++)
            if (Announcements[i].Id <= id) Announcements[i] = Announcements[i] with { IsUnread = false };
        RecentAnnouncements.Clear(); foreach (var item in Announcements.Take(2)) RecentAnnouncements.Add(item);
        var count = Announcements.Count(x => x.IsUnread); UnreadText = count > 0 ? $"{count} 条未读" : "全部已读";
    }

    public void ReportFailure(string message, bool expired = false)
    {
        IsStale = true; NeedsLogin = expired || NeedsLogin;
        StatusTitle = expired ? "需要重新登录" : "暂时无法同步";
        StatusDetail = message;
    }

    public void ClearAccount()
    {
        _subscriptions = null; _stats = null; HasData = false; HasStats = false; IsDemo = false;
        NeedsLogin = true; IsStale = false; IsLoading = false;
        Plans.Clear(); SelectablePlans.Clear(); SelectedPlan = null;
        ChartBars.Clear(); Announcements.Clear(); RecentAnnouncements.Clear();
        UserName = "你的桌面用量助手"; UserEmail = "连接账号，随时掌握剩余额度"; AvatarText = "K";
        CreditText = WelfareText = RequestsText = TokensText = CostText = SuccessRateText = InputTokensText = OutputTokensText = "—";
        LastUpdatedText = "等待首次同步"; StatusTitle = "尚未连接";
        StatusDetail = "使用网站现有的 GitHub 登录连接账号。";
        StatsNote = "连接账号后加载今日统计。"; SelectedTab = 0; ProjectQuota();
    }
}
