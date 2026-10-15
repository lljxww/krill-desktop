# Krill Desktop

Krill 个人账号用量助手，提供 Windows 与 macOS 两套原生客户端：

- **Windows**：C# / .NET 10 / WPF，桌面悬浮球，支持贴边收成进度竖条，点击展开详情。
- **macOS**：Swift 6 / SwiftUI，菜单栏进度环，点击展开详情面板。macOS 14 起运行；macOS 26 上可为导航与工具按钮使用 Liquid Glass。

两端均显示个人资料、套餐周期剩余、充值余额、福利余额、结转额度、今日统计和公告，使用网站现有 GitHub 登录，并可跳转官网管理账号。详情面板沿用相同的五个页签与深色薄荷绿配色。

**macOS 当前为在 Windows 上编写的源代码版本，尚未经过 Apple Swift 编译、链接与真机验证。** 仓库提供迁移到 Mac 后的构建脚本、Swift 测试、应用包配置及验收步骤。

## 项目结构

```text
apps/
  windows/
    src/Krill.Core/             C# 协议客户端、模型、额度与展示规则
    src/Krill.Desktop/          WPF 悬浮球、详情面板、登录与托盘
    tests/Krill.Core.Tests/     Windows 核心契约检查
  macos/
    Package.swift              Swift Package，原生 macOS 可执行目标
    Sources/KrillCore/         Swift 协议客户端、模型、额度与展示规则
    Sources/KrillMac/
      App/                    应用入口、菜单栏图标与弹出面板
      Services/               状态管理、钥匙串、WKWebView 登录
      Views/                  SwiftUI 五页详情面板与主题
    Tests/KrillCoreTests/      Swift 契约测试
    Support/Info.plist         .app 包配置与菜单栏应用标识
contracts/fixtures/            两端共用的虚构 JSON 样例，无账号凭据
docs/                         平台开发、协议与迁移说明
scripts/windows/              Windows 构建验证与发布
scripts/macos/                macOS 验证与 .app 打包
Krill.Desktop.slnx             Windows 解决方案入口
Directory.Build.props         Windows .NET 公共构建配置
global.json                   Windows .NET SDK 选择
```

平台代码按原生语言分别维护；[协议与展示约定](docs/protocol.md)和 `contracts/fixtures` 用于对齐接口字段与金额计算。

## Windows 开发

从仓库根目录执行，需要 .NET 10 SDK：

```powershell
dotnet build Krill.Desktop.slnx -c Release
dotnet run --project apps/windows/src/Krill.Desktop -- --demo
.\scripts\verify.ps1
.\scripts\publish.ps1
```

原有解决方案入口与根目录 PowerShell 脚本入口继续保留。完整使用、登录、数据目录和发布说明见 [Windows 文档](docs/windows.md)。

## macOS 开发

将完整仓库复制到 Mac，安装 Xcode 与命令行工具，然后从仓库根目录执行：

```bash
# 编译并执行 Swift 核心测试，再编译 macOS UI
bash scripts/macos/verify.sh

# 创建本机架构的应用包
bash scripts/macos/build.sh debug

# 直接运行 Swift Package 的演示模式（不支持完整 .app 登录项行为）
swift run --package-path apps/macos KrillMac --demo
```

建议使用 Xcode 26 或更新版本，以编译 Liquid Glass 分支。也可以用 Xcode 打开 `apps/macos/Package.swift` 继续开发。具体的 .app 启动方式、签名说明和迁移验收见 [macOS 开发文档](docs/macos.md)。

## 数据与验证边界

凭据只保存在本机：Windows 使用当前用户 DPAPI，macOS 使用钥匙串。两端登录窗口都使用独立临时浏览会话，仅在精确的 `https://www.krill-code.com` 来源读取 Krill 会话，再调用 `/api/auth/me` 校验。接口请求不会自动跟随重定向；公告只显示原生文本摘要。

`--demo` 使用虚构数据和独立配置目录。`record.har` 仅作为本地协议参考，已忽略，不参与构建或打包。请不要将个人 HAR、会话、授权码或 token 提交到仓库。

Windows 的实际验证结果与 macOS 上尚待完成的验证分别记录在平台文档中。接口来自官网当前实现，协议变化时需要同步更新两端客户端与共用样例。自动更新和跨设备会话同步尚未实现。
