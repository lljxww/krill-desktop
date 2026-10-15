using System.Text.Json;
using System.Windows.Threading;
using Krill.Core;
using Krill.Desktop.Services;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Krill.Desktop.Views;

public partial class LoginWindow : Window
{
    private readonly Func<string, CancellationToken, Task> _acceptSession;
    private readonly LocalStorage _storage;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private bool _checking;
    private bool _closed;
    private string? _rejectedToken;

    public LoginWindow(LocalStorage storage, Func<string, CancellationToken, Task> acceptSession)
    {
        InitializeComponent(); _storage = storage; _acceptSession = acceptSession;
        _poll.Tick += async (_, _) => await CheckSessionAsync();
        Loaded += async (_, _) => await InitializeAsync();
        Closed += (_, _) => { _closed = true; _poll.Stop(); _lifetime.Cancel(); Browser.Dispose(); _lifetime.Dispose(); };
    }

    private async Task InitializeAsync()
    {
        try
        {
            // The WebView is only used for authentication. InPrivate leaves no reusable
            // GitHub or Krill browser session behind after this window is disposed.
            Browser.CreationProperties = new CoreWebView2CreationProperties
            { UserDataFolder = Path.Combine(_storage.DirectoryPath, "Browser"), IsInPrivateModeEnabled = true };
            await Browser.EnsureCoreWebView2Async();
            if (_closed) return;
            var core = Browser.CoreWebView2;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.DownloadStarting += (_, e) => e.Cancel = true;
            core.NavigationStarting += (_, e) =>
            {
                if (!IsAllowedNavigation(e.Uri)) { e.Cancel = true; StatusText.Text = "登录窗口仅用于 Krill 与 GitHub 的登录页面。"; }
            };
            core.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (IsAllowedNavigation(e.Uri)) core.Navigate(e.Uri);
            };
            core.NavigationCompleted += async (_, e) =>
            {
                if (!e.IsSuccess) StatusText.Text = "页面加载失败，可在页面中重试，或关闭后重新连接。";
                else await CheckSessionAsync();
            };
            core.Navigate("https://www.krill-code.com/login");
            StatusText.Text = "请在页面中选择 GitHub 登录。首次连接需要在此窗口登录 GitHub。";
            _poll.Start();
        }
        catch (WebView2RuntimeNotFoundException)
        { StatusText.Text = "此设备缺少 Microsoft Edge WebView2 Runtime。安装后重新连接即可。"; RuntimeButton.Visibility = Visibility.Visible; }
        catch (Exception e)
        { _storage.Log("login_initialization_failed", e); if (!_closed) StatusText.Text = "登录窗口初始化失败，请关闭后重试。"; }
    }

    internal static bool IsAllowedNavigation(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps && uri.Port == 443 && string.IsNullOrEmpty(uri.UserInfo) &&
        (KrillApiClient.IsTrustedPage(uri) || uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase));

    private async Task CheckSessionAsync()
    {
        if (_checking || _closed || Browser.CoreWebView2 is null || !KrillApiClient.IsTrustedPage(Browser.Source)) return;
        _checking = true;
        try
        {
            // Check origin inside the script as well, because navigation may race the await.
            var result = await Browser.ExecuteScriptAsync("(() => location.origin === 'https://www.krill-code.com' ? {origin: location.origin, token: localStorage.getItem('krill_jwt')} : null)()");
            using var document = JsonDocument.Parse(result);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("origin", out var origin) || origin.GetString() != "https://www.krill-code.com" ||
                !root.TryGetProperty("token", out var value) || value.ValueKind != JsonValueKind.String) return;
            var token = value.GetString();
            if (!KrillApiClient.IsPlausibleSession(token) || token == _rejectedToken || !KrillApiClient.IsTrustedPage(Browser.Source)) return;
            StatusText.Text = "登录完成，正在确认账号…";
            try
            {
                await _acceptSession(token!, _lifetime.Token);
                if (!_closed) Close();
            }
            catch (KrillApiException e)
            {
                // A network interruption should be retried; a rejected credential should
                // only be retried when the site has issued a different credential.
                if (e.Failure == ApiFailure.SessionExpired) _rejectedToken = token;
                if (!_closed) StatusText.Text = e.FriendlyMessage;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        { _storage.Log("login_session_check_failed", e); if (!_closed) StatusText.Text = "暂时无法确认登录，请稍后重试。"; }
        finally { _checking = false; }
    }

    private void RuntimeClick(object sender, RoutedEventArgs e)
    { try { BrowserLauncher.OpenRuntimeDownload(); } catch (Exception error) { _storage.Log("runtime_link_failed", error); } }
}
