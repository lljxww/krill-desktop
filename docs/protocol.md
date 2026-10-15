# 两端协议与展示约定

网站来源固定为 `https://www.krill-code.com`。这份约定来自当前 Windows 实现与已有 HAR 分析；并非网站对外承诺的稳定 API。

## 请求

| 用途 | 方法与路径 | 认证 | 请求体 |
|---|---|---|---|
| 确认身份 | `GET /api/auth/me` | Krill 会话 | 无 |
| 套餐、额度、余额 | `GET /api/subscription` | Krill 会话 | 无 |
| 今日统计 | `POST /api/request-logs/stats` | Krill 会话 | `start_time`、`end_time`，UTC ISO8601 |
| 公告 | `GET /api/public/announcements` | 无 | 无 |

已认证请求同时携带 `Authorization: Bearer …` 与 `Cookie: krill_jwt=…`，沿用网站现有要求。语言头为 `X-Language: zh-CN`、`Accept-Language: zh-CN`。公共公告请求不携带账号会话。客户端只提供固定接口与官网跳转枚举，不接受外部传入的任意凭据目标地址。

响应外层为 `{ "success": true, "code": 200, "data": ... }`。HTTP 401 或外层业务 `code: 401` 视为会话失效；403、429、网络错误和数据格式错误分别处理。拒绝自动 HTTP 重定向，每份响应最多读取 8 MiB。不要在异常、日志或调试描述中包含 token 或完整响应。

## 金额与额度

- 服务端金额可能为 JSON number、数字字符串或 null；Windows 使用 `decimal`，Swift 使用 `Decimal`。缺失值为未知，显示 `—`。
- 当前周期使用 `quota.daily_limit_usd`、`used_usd`、`remaining_usd`；即使是周 / 月套餐，额度上限字段也可能仍叫 `daily_limit_usd`。不要改用订阅生命周期的 `total_*` 字段。
- `billing_type` 为 `usd_daily` / `usd_weekly` / `usd_monthly` 时分别显示本日 / 本周 / 本月额度；其他金额型周期显示“当前周期额度”。
- `request_count` 使用 `limit_requests` / `used_requests` / `remaining_requests`，单位为次。积分套餐使用 `limit_credits`（缺失时回退到 `plan.total_credits`）、`used_credits` / `remaining_credits`。未知字段不推断额度。
- 剩余百分比 = `remaining / limit × 100`，限制在 0–100；缺失剩余或上限不大于 0 时百分比未知。只在绘制时用空进度表示未知，文字仍显示 `—`。
- `forwarded_remaining_usd` 单独列为结转，不并入当前周期剩余百分比。充值余额和福利余额分别展示。
- 套餐按 `consume_priority` 升序排列，`status == active` 且尚未到期的套餐可选。恢复已选 ID，找不到时选择第一个有效套餐；没有有效套餐时显示充值余额。macOS 菜单栏图标此时使用金额标志，设置中可同时显示余额数字。
- 重置和到期时间来自服务端 ISO8601 字段，转换为本机时区。今日统计范围为本机当天零点到当前时间，按各时刻自己的时区偏移转换为 UTC，覆盖夏令时变化。

## 刷新与会话

默认后台 60 秒；展开详情时核心数据至多间隔 30 秒。统计在详情展开后加载；公告约每 10 分钟检查一次。失败按次数增加等待，最长约 300 秒；失败时保留当前进程中上次成功的数据，并标记离线。退出 / 切换账号时增加会话代次并取消旧任务，旧响应不得写入新账号面板。

GitHub 登录沿用网站的 `/login` 页面。Windows 使用 WebView2 InPrivate；macOS 使用 `WKWebsiteDataStore.nonPersistent()`。顶层导航仅允许精确 Krill 来源与 `https://github.com`（默认 HTTPS 端口，无 user info）。在读取 `localStorage.krill_jwt` 时同时检查浏览器 URL 与脚本内 `location.origin`，再通过账号接口确认身份。客户端不注册自己的 GitHub OAuth 应用，不接收或保存 GitHub 密码。

正常登录会话只存在 DPAPI / Keychain 中；偏好 JSON 不存 token。演示模式不加载真实会话、不请求个人账号接口。两个操作系统的会话与设置分别维护。

## 共用样例

`contracts/fixtures/subscriptions.json`、`stats.json` 为手工创建的虚构数据，不含真实账号。Windows 契约程序和 Swift `ProtocolTests` 读取同一组文件，验证：金额字符串、当前周期与生命周期区分、结转独立、按次 / 积分额度、未知值、统计小金额精度。

修改展示规则时同时更新两端实现、共用样例和断言。Swift 测试读取仓库内的相对路径，迁移时应复制完整仓库。
