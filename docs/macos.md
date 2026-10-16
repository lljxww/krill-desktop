# macOS 开发与迁移

## 当前状态

已在 Apple Silicon、macOS 27.0.1 和 Apple Swift 6.4 环境通过核心与 macOS 回归测试、Release 编译和 ad-hoc 签名检查。真实账号登录、旧版 macOS、Intel 架构及完整 UI 验收仍待完成。Windows 上可运行的语法树检查只能检查 Swift 语法结构，不能替代 Apple SDK 类型检查与实际运行。

运行最低版本为 macOS 14。`Package.swift` 使用 Swift 6 工具链与 Swift 6 语言模式，无第三方依赖。建议 Xcode 26 或更新版本，以使用 macOS 26 SDK 与 Liquid Glass。Liquid Glass 代码同时使用编译器和运行时版本判断；较早工具链 / 系统使用 SwiftUI 材质背景。

Windows 上可用 `uv run scripts/macos/check_source.py` 重跑 Swift / Bash 语法树、plist 和虚构样例的静态检查，输出 `artifacts/macos-source-validation.json`。该脚本的 Python 解析器依赖仅用于开发检查，不属于 macOS 应用运行依赖。

## 代码入口

| 位置 | 作用 |
|---|---|
| `Sources/KrillMac/App/KrillApp.swift` | AppKit 应用入口与菜单栏生命周期，无独立窗口场景 |
| `App/MenuBarController.swift`、`App/MenuBarIcon.swift` | `NSStatusItem`、磷虾 Logo 与动态进度环、右键菜单、`NSPopover` |
| `Views/DashboardView.swift` | 概览、套餐、今日用量、公告、设置 |
| `Views/Theme.swift` | 与 Windows 对齐的配色、卡片、额度环；局部玻璃效果 |
| `Services/DashboardStore.swift` | 主线程状态、刷新、离线 / 失效处理、登录项 |
| `Services/LoginWindowController.swift` | 临时 `WKWebView` 登录、来源校验与会话确认 |
| `Services/LocalStorage.swift` | JSON 偏好、Keychain 会话 |
| `Sources/KrillCore/` | Foundation 协议、模型、金额计算、演示样例 |
| `Tests/KrillCoreTests/`、`Tests/KrillMacTests/` | 核心契约、菜单栏图标绘制与 WebKit 导航回调注册测试 |

AppKit 负责应用事件循环、菜单栏、窗口壳、浏览器和系统存储；详情内容通过 `NSHostingController` 使用 SwiftUI。应用不注册 SwiftUI `App`、`Settings` 或 `WindowGroup` 场景，因此不会生成空白默认窗口；设置位于详情面板内。应用不显示 Dock 图标，关闭面板后继续常驻菜单栏。22 点菜单栏图标中心是与应用图标一致的单色几何磷虾 Logo，外圈从顶部顺时针表示所选套餐的剩余额度：100% 为完整圆环，0% 仅显示淡色底环；只有充值余额或额度未知时也只显示底环。图标用系统模板色适应明暗背景；鼠标悬停显示金额、套餐、连接状态和更新时间。设置中可在图标旁显示百分比 / 余额。未登录或会话失效时停止显示额度弧，右下角显示空心提示点；离线保留上次额度弧并显示实心提示点。

详情面板宽约 500 点，高度按当前屏幕工作区限制，内容可滚动；保留 Windows 的五个页签、金额卡片、额度环、统计柱图和底部网站入口。支持固定面板、Esc 收起、手动刷新、选择套餐、公告未读与登录启动。Liquid Glass 仅用于导航与工具控件，金额信息使用深色卡片维持可读性。

导航页签整个按钮区域均可点击，高度至少 40 点。用量页的时段费用图按时间排序，最多展示最近 48 个时段，下方显示本机时区的时间刻度；鼠标在绘图区移动时会显示对应时段的竖向定位线，并在图表上方即时显示日期、时间、费用和请求数。整列空白和零费用柱也能触发提示，未知费用显示为 `—`；移出图表后清除定位，提示区域预留固定高度以避免布局跳动。

## 第一次在 Mac 上运行

将整个仓库复制到 Mac；可跳过 `.vs`、Windows `bin` / `obj`、`dist`、`artifacts` 和个人 HAR。保留根目录 `contracts/fixtures`，Swift 测试会读取它。不要复制 Windows 本地账号凭据。

安装 Xcode，首次启动接受协议并完成组件安装，选择其命令行工具。然后在仓库根目录执行：

```bash
xcode-select -p
swift --version
bash scripts/macos/verify.sh
bash scripts/macos/build.sh debug
```

构建脚本生成本机架构（Apple Silicon 为 `arm64`，Intel 为 `x86_64`）的完整应用包，例如：

```bash
open "dist/Krill.Desktop-0.1.4-macos-arm64-debug/Krill Desktop.app" --args --demo
```

关闭演示实例后，去掉 `--demo` 启动账号模式。正常模式、演示模式和登录启动均默认只在顶部菜单栏常驻，不自动展开详情；再次打开已运行的应用也不弹出面板。点击菜单栏图标，或在其右键菜单选择“打开详情”，即可查看面板。旧的 `--background` 参数仍可使用，但无需再显式传入。

Xcode 可直接打开 `apps/macos/Package.swift`，选择 `KrillMac` 可执行 Scheme 进行开发。直接使用 `swift run --package-path apps/macos KrillMac --demo` 适合查看界面；登录项注册应通过脚本生成的 `.app` 测试。原始命令行二进制不会自动包含 `Info.plist` 的应用包身份。

## 打包与签名

应用图标位于 `apps/macos/Support/Assets/`，包含可编辑的 SVG、1024 像素 PNG 和多尺寸 ICNS。修改 SVG 后，运行 `swift scripts/macos/generate_icon.swift` 重新生成图标，再执行打包；脚本会将 ICNS 放入应用包并一起签名。设计来源与生成方式见该目录的 `README.md`。

```bash
# 本机架构 Release
bash scripts/macos/build.sh release

# Intel + Apple Silicon 通用包
bash scripts/macos/build.sh release --universal

# 可选：使用本机已配置的 Developer ID 签名身份
KRILL_SIGN_IDENTITY="Developer ID Application: Your Name (TEAMID)" \
  bash scripts/macos/build.sh release --universal
```

脚本将可执行文件和 `Support/Info.plist` 放入标准 `.app/Contents` 结构，默认使用本机 ad-hoc 签名；提供身份时使用 Hardened Runtime 与时间戳。公证、安装镜像、App Store 分发与自动更新尚未实现。开发时应用包位置和签名变化可能触发钥匙串重新授权，应在 Mac 上实际确认；不要用日志打印 token 来诊断。

使用 `SMAppService.mainApp` 注册登录项。建议把应用复制到固定的 Applications 目录后再开启登录启动；需要系统批准时，设置页提供打开“登录项”的按钮。演示 / 正常模式请逐个退出再启动，避免同时显示多个菜单栏图标。

## 本地数据

- 正常偏好：`~/Library/Application Support/KrillDesktop/Account/settings.json`
- 演示偏好：`~/Library/Application Support/KrillDesktop/Demo/settings.json`
- 正常会话：Keychain 通用密码，service 为 `com.krill.desktop.macos.account`，account 为 `krill-session`。
- 演示 CLI 模式使用单独 Keychain service，且不会读取或保存真实登录会话。
- 登录 WKWebView 为非持久会话，关闭后不保留可复用的 GitHub 浏览会话；Safari / 默认浏览器的官网登录状态独立。

## 迁移后的验收顺序

1. 运行 `verify.sh`，确认 Swift 6 并发检查、Apple SDK 编译、所有 Swift 测试和 AppKit 入口检查通过；入口检查会拒绝重新引入 SwiftUI `App` / `Settings` 窗口场景。再运行 `build.sh` 验证 .app 生成与签名检查。
2. 用 `--demo` 检查五个页签、套餐切换与进度环、未知 / 零额度、滚动、Esc、右键菜单、固定面板与退出。
3. 检查浅色 / 深色菜单栏、外接屏、Retina 缩放、菜单栏自动隐藏和屏幕空间不足时的弹出位置；检查 macOS 26 玻璃分支以及旧系统材质回退。
4. 在官网登录窗口完成 GitHub 和可能的二次验证，确认 `/api/auth/me` 与套餐接口成功，关闭并重新启动后恢复会话。若网站拒绝嵌入式浏览器，需按网站支持的授权方式重新设计登录入口，当前不能视为已验证。
5. 检查断网保留数据与重试间隔、睡眠唤醒、401 失效、重新登录、退出清除钥匙串，以及旧请求不会覆盖新会话。
6. 在固定安装位置检查登录启动的启用、系统批准和关闭；确认首次启动、登录启动及再次打开应用均只显示菜单栏图标，点击图标后才展开面板。

Apple 参考：[NSPopover](https://developer.apple.com/documentation/appkit/nspopover)、[WKWebView](https://developer.apple.com/documentation/webkit/wkwebview)、[Liquid Glass](https://developer.apple.com/documentation/swiftui/applying-liquid-glass-to-custom-views)、[SMAppService](https://developer.apple.com/documentation/servicemanagement/smappservice)。
