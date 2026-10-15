using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Krill.Core;

var tests = new (string Name, Func<Task> Run)[]
{
    ("Shared Windows and macOS subscription fixtures preserve projection rules", () => Sync(() =>
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "subscriptions.json"));
        var response = JsonSerializer.Deserialize<ApiEnvelope<SubscriptionResponse>>(json, KrillJson.Options)!;
        Equal(true, response.Success); var data = response.Data!;
        Equal(24.80m, data.CreditBalanceUsd); Equal(3.50m, data.WelfareBalanceUsd);
        var weekly = QuotaProjection.From(data.Subscriptions[0]);
        Equal("本周额度", weekly.Cycle); Equal(100m, weekly.Limit); Equal(72.60m, weekly.Remaining);
        Equal(72.6, weekly.Percent); Equal(8.20m, weekly.Forwarded);
        Equal(75d, QuotaProjection.From(data.Subscriptions[1]).Percent);
        Equal(80d, QuotaProjection.From(data.Subscriptions[2]).Percent);
        Equal(null, QuotaProjection.From(data.Subscriptions[3]).Percent);
    })),
    ("Shared Windows and macOS statistics fixture preserves numeric strings", () => Sync(() =>
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "stats.json"));
        var stats = JsonSerializer.Deserialize<ApiEnvelope<RequestStats>>(json, KrillJson.Options)!.Data!;
        Equal(128L, stats.TotalRequests); Equal(1_284_600L, stats.TotalTokens);
        Equal("$0.0007", DisplayFormat.Cost(stats.TotalCostUsd)); Equal(9L, stats.Trend[0].RequestCount);
    })),
    ("HAR-shaped amounts deserialize exactly and missing balances stay unknown", () => Sync(() =>
    {
        var data = JsonSerializer.Deserialize<SubscriptionResponse>("""
        {"credit_balance_usd":"24.80","subscriptions":[{"subscription_id":1,"status":"active","plan":{"name":"Sample","billing_type":"usd_weekly"},"quota":{"daily_limit_usd":"100.0000","used_usd":"27.4000","remaining_usd":"72.6000","forwarded_remaining_usd":"8.20","window_reset_at":"2026-10-19T00:00:00+08:00"}}]}
        """, KrillJson.Options)!;
        Equal(24.80m, data.CreditBalanceUsd); Equal(null, data.WelfareBalanceUsd);
        var quota = QuotaProjection.From(data.Subscriptions[0]);
        Equal(72.60m, quota.Remaining); Equal(8.20m, quota.Forwarded); Equal("本周额度", quota.Cycle);
        Equal(72.6, quota.Percent); Equal("$72.60", DisplayFormat.Amount(quota, quota.Remaining));
        Equal(TimeSpan.FromHours(8), quota.ResetAt!.Value.Offset);
    })),
    ("Current window and lifetime totals remain separate", () => Sync(() =>
    {
        var s = DemoData.Subscriptions(DateTimeOffset.Now).Subscriptions[0];
        s.TotalLimitUsd = 400; s.TotalRemainingUsd = 200;
        Equal(72.60m, QuotaProjection.From(s).Remaining);
        Equal(100m, QuotaProjection.From(s).Limit);
    })),
    ("Zero and missing quotas do not create a percentage", () => Sync(() =>
    {
        Equal(null, new QuotaProjection("未知", 0, 0, 0, null, null, true, "USD").Percent);
        Equal(null, new QuotaProjection("未知", 100, 0, null, null, null, true, "USD").Percent);
        Equal(0d, new QuotaProjection("额度", 100, 110, -10, null, null, true, "USD").Percent);
        Equal(100d, new QuotaProjection("额度", 100, 0, 120, null, null, true, "USD").Percent);
    })),
    ("Unsupported request-count fields stay unknown", () => Sync(() =>
    {
        var q = QuotaProjection.From(new Subscription { Plan = new() { BillingType = "request_count" } });
        Equal(false, q.IsMoney); Equal(null, q.Percent); Equal("—", DisplayFormat.Amount(q, q.Remaining));
    })),
    ("Currency display is independent of desktop number culture", () => Sync(() =>
    {
        var previous = CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE"); Equal("$1,234.50", DisplayFormat.Money(1234.5m)); Equal("$0.0007", DisplayFormat.Cost(.0007m)); }
        finally { CultureInfo.CurrentCulture = previous; }
    })),
    ("Local-day requests use the correct UTC boundary", () => Sync(() =>
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("China Standard Time");
        var range = DisplayFormat.LocalDayRange(DateTimeOffset.Parse("2026-10-15T00:30:00+08:00"), zone);
        Equal(DateTimeOffset.Parse("2026-10-14T16:00:00Z"), range.From);
        Equal(DateTimeOffset.Parse("2026-10-14T16:30:00Z"), range.To);
    })),
    ("Daylight-saving transition uses midnight's own offset", () => Sync(() =>
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        var range = DisplayFormat.LocalDayRange(DateTimeOffset.Parse("2026-03-08T12:00:00-04:00"), zone);
        Equal(DateTimeOffset.Parse("2026-03-08T05:00:00Z"), range.From);
    })),
    ("Session extraction accepts only the exact HTTPS origin", () => Sync(() =>
    {
        Equal(true, KrillApiClient.IsTrustedPage(new("https://www.krill-code.com/app")));
        foreach (var uri in new[] { "http://www.krill-code.com/app", "https://www.krill-code.com.evil.example/app", "https://www.krill-code.com:444/app", "https://user@www.krill-code.com/app", "https://github.com/app" })
            Equal(false, KrillApiClient.IsTrustedPage(new(uri)));
    })),
    ("Credentials reject header injection and malformed values", () => Sync(() =>
    {
        Equal(true, KrillApiClient.IsPlausibleSession("synthetic.header.signature"));
        foreach (var token in new[] { "a", "abc.def", "synthetic.header.signature\r\nX-Injected: yes", "synthetic.header.signature extra" })
            Equal(false, KrillApiClient.IsPlausibleSession(token));
    })),
    ("Stats requests preserve method, range, Bearer and same-site cookie", async () =>
    {
        using var http = new HttpClient(new Handler(async request =>
        {
            Equal("https://www.krill-code.com/api/request-logs/stats", request.RequestUri!.AbsoluteUri);
            Equal(HttpMethod.Post, request.Method);
            Equal("Bearer", request.Headers.Authorization!.Scheme);
            Equal("synthetic.header.signature", request.Headers.Authorization.Parameter);
            Equal("krill_jwt=synthetic.header.signature", request.Headers.GetValues("Cookie").Single());
            using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
            Equal("2026-10-14T16:00:00.0000000+00:00", json.RootElement.GetProperty("start_time").GetString());
            return Json("{\"success\":true,\"code\":0,\"data\":{\"total_requests\":5,\"total_cost_usd\":\"0.0007\"}}");
        }));
        var result = await new KrillApiClient(http).GetStatsAsync("synthetic.header.signature", DateTimeOffset.Parse("2026-10-15T00:00:00+08:00"), DateTimeOffset.Parse("2026-10-15T01:00:00+08:00"));
        Equal(5L, result.TotalRequests); Equal(.0007m, result.TotalCostUsd);
    }),
    ("Public announcements do not transmit an account session", async () =>
    {
        using var http = new HttpClient(new Handler(request =>
        {
            Equal(null, request.Headers.Authorization); Equal(false, request.Headers.Contains("Cookie"));
            return Task.FromResult(Json("{\"success\":true,\"data\":{\"items\":[]}}"));
        }));
        Equal(0, (await new KrillApiClient(http).GetAnnouncementsAsync()).Items.Count);
    }),
    ("HTTP 401 and envelope 401 require login", async () =>
    {
        using var first = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        await Fails(ApiFailure.SessionExpired, () => new KrillApiClient(first).GetProfileAsync("synthetic.header.signature"));
        using var second = new HttpClient(new Handler(_ => Task.FromResult(Json("{\"success\":false,\"code\":401}"))));
        await Fails(ApiFailure.SessionExpired, () => new KrillApiClient(second).GetProfileAsync("synthetic.header.signature"));
    }),
    ("429, forbidden, redirects and HTML remain distinct failures", async () =>
    {
        foreach (var (status, expected) in new[] { ((HttpStatusCode)429, ApiFailure.RateLimited), (HttpStatusCode.Forbidden, ApiFailure.Forbidden), (HttpStatusCode.Redirect, ApiFailure.Service) })
        {
            using var http = new HttpClient(new Handler(_ => Task.FromResult(new HttpResponseMessage(status))));
            await Fails(expected, () => new KrillApiClient(http).GetProfileAsync("synthetic.header.signature"));
        }
        using var invalid = new HttpClient(new Handler(_ => Task.FromResult(Json("<html>not JSON</html>"))));
        await Fails(ApiFailure.InvalidResponse, () => new KrillApiClient(invalid).GetProfileAsync("synthetic.header.signature"));
    }),
    ("Failed business envelopes do not yield an empty financial result", async () =>
    {
        using var http = new HttpClient(new Handler(_ => Task.FromResult(Json("{\"success\":false,\"code\":500,\"data\":{}}"))));
        await Fails(ApiFailure.InvalidResponse, () => new KrillApiClient(http).GetSubscriptionsAsync("synthetic.header.signature"));
    }),
    ("Announcements are plain text and bounded", () => Sync(() =>
    {
        Equal("Hello & world", DisplayFormat.PlainText("<script>ignored()</script><b>Hello &amp; world</b>"));
        Equal("abc…", DisplayFormat.PlainText("abcdef", 3));
    }))
};

var failures = 0;
if (args.Contains("--har"))
{
    try
    {
        var index = Array.IndexOf(args, "--har");
        using var archive = JsonDocument.Parse(await File.ReadAllTextAsync(args[index + 1]));
        var verified = 0;
        foreach (var entry in archive.RootElement.GetProperty("log").GetProperty("entries").EnumerateArray())
        {
            var path = new Uri(entry.GetProperty("request").GetProperty("url").GetString()!).AbsolutePath;
            if (path is not ("/api/subscription" or "/api/request-logs/stats" or "/api/public/announcements")) continue;
            var content = entry.GetProperty("response").GetProperty("content");
            var body = content.GetProperty("text").GetString()!;
            if (content.TryGetProperty("encoding", out var encoding) && encoding.GetString() == "base64")
                body = Encoding.UTF8.GetString(Convert.FromBase64String(body));
            switch (path)
            {
                case "/api/subscription":
                    var subscription = JsonSerializer.Deserialize<ApiEnvelope<SubscriptionResponse>>(body, KrillJson.Options)!;
                    Equal(true, subscription.Success);
                    foreach (var item in subscription.Data!.Subscriptions) _ = QuotaProjection.From(item).Percent;
                    break;
                case "/api/request-logs/stats":
                    Equal(true, JsonSerializer.Deserialize<ApiEnvelope<RequestStats>>(body, KrillJson.Options)!.Success); break;
                case "/api/public/announcements":
                    Equal(true, JsonSerializer.Deserialize<ApiEnvelope<AnnouncementsResponse>>(body, KrillJson.Options)!.Success); break;
            }
            verified++;
        }
        Equal(true, verified > 0);
        Console.WriteLine($"PASS local HAR response compatibility ({verified} responses; no credentials read or transmitted)");
    }
    catch (Exception error) { failures++; Console.WriteLine("FAIL local HAR response compatibility (" + error.GetType().Name + ")"); }
}
if (args.Contains("--live-public"))
{
    try
    {
        using var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false }) { Timeout = TimeSpan.FromSeconds(20) };
        _ = await new KrillApiClient(http).GetAnnouncementsAsync();
        Console.WriteLine("PASS live public announcements (anonymous; no account session)");
    }
    catch (Exception error) { failures++; Console.WriteLine("FAIL live public announcements (" + error.GetType().Name + ")"); }
}
foreach (var test in tests)
{
    try { await test.Run(); Console.WriteLine("PASS " + test.Name); }
    catch (Exception error) { failures++; Console.WriteLine("FAIL " + test.Name + " (" + error.GetType().Name + ")"); }
}
Console.WriteLine($"{tests.Length - failures}/{tests.Length} passed");
return failures == 0 ? 0 : 1;

static Task Sync(Action action) { action(); return Task.CompletedTask; }
static void Equal<T>(T expected, T actual)
{ if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new InvalidOperationException("Assertion failed."); }
static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
static async Task Fails(ApiFailure expected, Func<Task> operation)
{
    try { await operation(); } catch (KrillApiException error) { Equal(expected, error.Failure); return; }
    throw new InvalidOperationException("Expected API failure.");
}
sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
}
