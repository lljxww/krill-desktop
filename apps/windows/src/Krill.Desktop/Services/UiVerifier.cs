using System.Text;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Krill.Core;

namespace Krill.Desktop.Services;

internal static class UiVerifier
{
    public static async Task RunAsync(AppController app, LocalStorage storage, string output)
    {
        Directory.CreateDirectory(output);
        var checks = new List<string>();
        var layouts = new List<object>();
        app.ViewModel.PanelPinned = true;
        app.ViewModel.AlwaysOnTop = false;
        app.Panel.Show();
        app.Panel.ClearInitialFocus();
        await app.Panel.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        if (!ReferenceEquals(System.Windows.Input.FocusManager.GetFocusedElement(app.Panel), app.Panel))
            throw new InvalidOperationException("Opening the panel focused a control.");
        checks.Add("panel_opens_without_control_focus");
        var names = new[] { "overview", "plans", "usage", "announcements", "settings" };
        for (var i = 0; i < names.Length; i++)
        {
            app.ViewModel.SelectedTab = i;
            await Task.Delay(120);
            await app.Panel.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            app.Panel.UpdateLayout();
            Capture(app.Panel, Path.Combine(output, names[i] + ".png"));
            layouts.Add(new { tab = names[i], isDemo = app.ViewModel.IsDemo,
                scrollBars = Descendants(app.Panel).OfType<ScrollBar>().Where(b => b.IsVisible).Select(b => new { b.Width, b.ActualWidth }).ToArray() });
            checks.Add("render_" + names[i]);
        }
        Capture(app.Orb, Path.Combine(output, "orb.png"));
        foreach (var side in new[] { OrbDockSide.Left, OrbDockSide.Right })
        {
            app.Orb.ExpandDocked();
            WindowPlacement.ResizeDockedOrb(app.Orb, Views.OrbWindow.BallSize, side);
            app.Orb.ConfigureDocking(enabled: true, snapNow: true);
            app.Orb.CollapseDocked();
            await app.Orb.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            if (!app.Orb.IsCollapsed || app.Orb.Width != Views.OrbWindow.BarWidth || app.Orb.DockSide != side)
                throw new InvalidOperationException("Docked strip geometry mismatch.");
            var saved = WindowPlacement.ExpandedOrbPosition(app.Orb);
            Capture(app.Orb, Path.Combine(output, "orb-docked-" + side.ToString().ToLowerInvariant() + ".png"));
            app.Orb.ExpandDocked();
            var expanded = WindowPlacement.ExpandedOrbPosition(app.Orb);
            if (app.Orb.IsCollapsed || app.Orb.Width != Views.OrbWindow.BallSize || Math.Abs(expanded.X - saved.X) > 1 || expanded.Y != saved.Y)
                throw new InvalidOperationException("Docked expansion changed the saved anchor.");
            app.Orb.CollapseDocked();
            app.Orb.ConfigureDocking(enabled: false, snapNow: false);
            if (app.Orb.IsCollapsed || app.Orb.DockSide != OrbDockSide.None || app.Orb.Width != Views.OrbWindow.BallSize)
                throw new InvalidOperationException("Disabling docking did not restore the ball.");
            checks.Add("dock_collapse_expand_disable_" + side.ToString().ToLowerInvariant());
        }
        if (app.ViewModel.OrbText != "73%" || app.ViewModel.PrimaryRemaining != "$72.60") throw new InvalidOperationException("Demo projection mismatch.");
        checks.Add("quota_projection");
        app.ViewModel.SelectedPlan = app.ViewModel.SelectablePlans.Single(p => p.Id == 2);
        if (app.ViewModel.PrimaryRemaining != "$16.80" || app.ViewModel.OrbText != "84%") throw new InvalidOperationException("Plan selection did not update the orb.");
        app.ViewModel.SelectedPlan = app.ViewModel.SelectablePlans.Single(p => p.Id == 1);
        checks.Add("plan_selection_updates_orb");
        var rolloverTime = DateTimeOffset.UtcNow;
        var exhausted = new Subscription { SubscriptionId = 301, Status = "active", ConsumePriority = 1,
            Plan = new Plan { Name = "Synthetic exhausted month", BillingType = "usd_monthly" },
            Quota = new Quota { DailyLimitUsd = 100, RemainingUsd = 0 } };
        var monthly = new Subscription { SubscriptionId = 302, Status = "active", ConsumePriority = 10,
            SubscriptionEndAt = rolloverTime.AddSeconds(10),
            Plan = new Plan { Name = "Synthetic usable month", BillingType = "usd_monthly" },
            Quota = new Quota { DailyLimitUsd = 100, RemainingUsd = 80 } };
        var upcoming = new Subscription { SubscriptionId = 303, Status = "active", ConsumePriority = 2,
            SubscriptionStartAt = rolloverTime.AddSeconds(10),
            Plan = new Plan { Name = "Synthetic upcoming month", BillingType = "usd_monthly" },
            Quota = new Quota { DailyLimitUsd = 100, RemainingUsd = 100 } };
        var dailyPlan = new Subscription { SubscriptionId = 304, Status = "active", ConsumePriority = 0,
            Plan = new Plan { Name = "Synthetic daily card", BillingType = "usd_daily" },
            Quota = new Quota { DailyLimitUsd = 20, RemainingUsd = 10 } };
        var monthlyResponse = new SubscriptionResponse { Subscriptions = [exhausted, monthly, upcoming, dailyPlan] };
        // First display the card while usable, then observe its exhaustion on the next response.
        exhausted.Quota.RemainingUsd = 10;
        app.ViewModel.ApplyCore(DemoData.Profile, new SubscriptionResponse { Subscriptions = [exhausted] }, demo: true, now: rolloverTime);
        exhausted.Quota.RemainingUsd = 0;
        app.ViewModel.ApplyCore(DemoData.Profile, monthlyResponse, demo: true, now: rolloverTime);
        if (app.ViewModel.SelectedPlan?.Id != 302 || storage.LoadSettings().SelectedSubscriptionId != 302 || app.ViewModel.OrbText != "80%")
            throw new InvalidOperationException("Monthly exhaustion did not persist a usable monthly selection.");
        if (app.ViewModel.Plans.Single(p => p.Id == 301) is not { State: "额度已用尽", IsActive: false, Opacity: < 1 })
            throw new InvalidOperationException("Exhausted card was not muted.");
        app.ViewModel.SelectedTab = 1;
        await app.Panel.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Capture(app.Panel, Path.Combine(output, "monthly-rollover.png"));
        checks.Add("monthly_exhaustion_switches_and_persists");
        app.ViewModel.AdvanceSubscriptionTime(rolloverTime.AddSeconds(10));
        if (app.ViewModel.SelectedPlan?.Id != 303 || storage.LoadSettings().SelectedSubscriptionId != 303 ||
            app.ViewModel.Plans.Single(p => p.Id == 302).State != "已到期")
            throw new InvalidOperationException("Monthly expiry did not switch to the newly effective card.");
        checks.Add("monthly_expiry_switches_without_network");
        app.UseDemo();
        if (!Views.LoginWindow.IsAllowedNavigation("https://github.com/login/oauth/authorize?state=synthetic") ||
            !Views.LoginWindow.IsAllowedNavigation("https://appleid.apple.com/auth/authorize") ||
            Views.LoginWindow.IsAllowedNavigation("http://www.krill-code.com/login")) throw new InvalidOperationException("Login navigation policy mismatch.");
        checks.Add("login_https_navigation");
        app.ViewModel.SelectedTab = 0;
        var previousAmount = app.ViewModel.PrimaryRemaining;
        app.ViewModel.ReportFailure("连接暂时中断，以下为上次成功读取的数据。");
        await Task.Delay(120);
        await app.Panel.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Capture(app.Panel, Path.Combine(output, "offline.png"));
        if (app.ViewModel.PrimaryRemaining != previousAmount || app.ViewModel.OrbCaption != "离线") throw new InvalidOperationException("Offline data lost.");
        checks.Add("offline_preserves_amount");
        app.ViewModel.ReportFailure("登录已失效，请重新连接账号。", expired: true);
        if (app.ViewModel.OrbText != "登录" || !app.ViewModel.NeedsLogin) throw new InvalidOperationException("Expired session state mismatch.");
        checks.Add("expired_session_requires_login");
        app.ViewModel.ClearAccount();
        await Task.Delay(120);
        await app.Panel.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Capture(app.Panel, Path.Combine(output, "welcome.png"));
        if (app.ViewModel.HasData || app.ViewModel.CreditText != "—" || app.ViewModel.Plans.Count != 0) throw new InvalidOperationException("Signout data not cleared.");
        checks.Add("signout_clears_view");

        const string testToken = "synthetic.header.signature";
        storage.SaveSession(testToken);
        if (storage.LoadSession() != testToken) throw new InvalidOperationException("Credential roundtrip failed.");
        if (Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(storage.DirectoryPath, "session.dat"))).Contains(testToken, StringComparison.Ordinal))
            throw new InvalidOperationException("Credential plaintext detected.");
        storage.DeleteSession();
        checks.Add("dpapi_encrypt_roundtrip_and_delete");
        await File.WriteAllTextAsync(Path.Combine(output, "verification.json"), JsonSerializer.Serialize(new
        { success = true, mode = "synthetic_demo_no_account_requests", checks, layouts, verifiedAt = DateTimeOffset.UtcNow }, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Capture(FrameworkElement visual, string path)
    {
        visual.UpdateLayout();
        var dpi = VisualTreeHelper.GetDpi(visual);
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(visual.ActualWidth * dpi.DpiScaleX), (int)Math.Ceiling(visual.ActualHeight * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i); yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
