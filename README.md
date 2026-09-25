# ProgramDock

[![License: MIT](https://img.shields.io/badge/License-MIT-green.svg)](LICENSE)
[![Platform: Windows x64](https://img.shields.io/badge/Platform-Windows%20x64-0078D4.svg)](https://learn.microsoft.com/dotnet/core/project-sdk/msbuild)

ProgramDock 是一个面向 Windows 10/11 的本地程序启动器。它把用户常用的程序快捷方式集中到一个高密度、可搜索的启动面板中，并提供收藏、最近使用、系统/辅助分类和托盘驻留能力。

## 功能

- 扫描所有可用固定磁盘，也支持自定义扫描目录。
- 默认索引 `.exe`、`.com`、`.bat`、`.cmd`、`.lnk`、`.msi`、`.msix` 和 `.appx`。
- 读取 Windows 卸载注册表中的已安装程序。
- 以用户桌面和 `文档\快捷方式` 中的快捷方式作为“直接使用”证据。
- 解析快捷方式的目标路径、参数和工作目录，合并相同启动签名的重复项。
- 将系统组件、驱动、运行时、安装器、命令行工具和构建辅助程序归入“系统/辅助”。
- 支持程序搜索、收藏、最近使用、隐藏、右键手动分类和启动。
- 高密度 8 列分页网格；搜索采用防抖和后台查询。
- 支持无边框窗口拖拽、双击最大化/还原、托盘驻留和全局快捷键。
- 扫描在后台执行，不需要管理员权限；权限不足和循环目录会安全跳过。
- 本地 JSON 缓存支持下次启动快速恢复目录。

## 直接使用分类

ProgramDock 默认显示“直接使用”分类。为了避免把磁盘角落里的大量辅助文件当成用户程序，直接使用列表只显示具有明确用户证据的项目：

1. 位于 `C:\Users\<用户名>\Documents\快捷方式` 或用户桌面中的快捷方式；
2. 快捷方式解析出的目标程序；
3. 已收藏或最近使用过的程序；
4. 用户通过右键菜单手动归类为“直接使用”的程序。

没有上述证据的项目会进入“系统/辅助”或“全部”视图。快捷方式的目标相同但启动参数不同，仍会保留为独立项目。

## 系统要求

- Windows 10 或 Windows 11，x64 架构；
- 自包含版本不需要预装 .NET Runtime；
- 框架依赖版本需要 .NET 8 Desktop Runtime。

## 下载和运行

Release 页面提供以下文件：

- `ProgramDock-1.0.0-win-x64-selfcontained.zip`：推荐下载，包含 .NET Runtime；
- `ProgramDock-1.0.0-win-x64-framework-dependent.zip`：体积更小，需要 .NET 8 Desktop Runtime；
- `SHA256SUMS.txt`：用于校验下载文件完整性。

解压自包含版本后，直接运行：

```text
ProgramDock.exe
```

首次启动默认读取已有本地缓存；点击“重新扫描”可以更新程序目录。启动时不想立即扫描固定磁盘时，可以使用：

```powershell
.\ProgramDock.exe --no-scan
```

## 从源码构建

需要 Windows 10/11 和 .NET 8 SDK：

```powershell
cd C:\path\to\ProgramDock

dotnet restore .\ProgramDock.sln
dotnet build .\ProgramDock.sln -c Release -p:Platform=x64
```

发布自包含版本：

```powershell
dotnet publish .\src\ProgramDock\ProgramDock.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:Platform=x64 -p:PublishTrimmed=false `
  -o .\artifacts\ProgramDock-win-x64-selfcontained
```

发布框架依赖版本：

```powershell
dotnet publish .\src\ProgramDock\ProgramDock.csproj `
  -c Release -r win-x64 --self-contained false `
  -p:Platform=x64 -p:PublishTrimmed=false `
  -o .\artifacts\ProgramDock-win-x64-framework-dependent
```

也可以使用项目脚本：

```powershell
.\package-release.ps1 -Version 1.0.0
.\verify-release.ps1 -Version 1.0.0
```

`package-release.ps1` 会生成两个 x64 压缩包、Release Notes 和 `SHA256SUMS.txt`；`verify-release.ps1` 会在发布前重新校验所有资产。

## 快捷键和窗口操作

| 操作 | 快捷键/方式 |
| --- | --- |
| 聚焦搜索 | `Ctrl+F` |
| 启动选中程序 | `Enter` 或双击图标 |
| 关闭/隐藏窗口 | `Esc` |
| 重新扫描 | `F5` |
| 移动窗口 | 拖动 Logo、标题栏或窗口空白区域 |
| 最大化/还原 | 双击标题栏 |
| 全局呼出 | 默认 `Ctrl+Alt+Space`，可在设置中修改 |

## 数据和隐私

ProgramDock 只在用户主动点击时启动程序。扫描结果、设置、收藏和异常日志保存在本机：

```text
%APPDATA%\ProgramDock\settings.json
%APPDATA%\ProgramDock\catalog.json
%APPDATA%\ProgramDock\crash.log
```

程序不会主动请求管理员权限，也不会上传目录清单、收藏或快捷方式信息。首次从旧版本升级时，旧版本的用户设置和目录缓存会自动迁移到新的 ProgramDock 数据目录。

## 项目结构

```text
ProgramDock/
├─ ProgramDock.sln
├─ build.ps1
├─ Create-Shortcut.ps1
├─ CHANGELOG.md
├─ LICENSE
├─ SECURITY.md
├─ CONTRIBUTING.md
├─ RELEASE_NOTES.md
├─ .github/workflows/release.yml
└─ src/ProgramDock/
   ├─ ProgramDock.csproj
   ├─ MainWindow.xaml / MainWindow.xaml.cs
   ├─ SettingsWindow.xaml / SettingsWindow.xaml.cs
   ├─ Models/
   ├─ Services/
   └─ Assets/ProgramDock.ico
```

## 当前限制

- 首次全盘扫描时间取决于磁盘数量和文件数量；
- 当前使用内存目录和 JSON 缓存，尚未使用 SQLite/FTS5 增量索引；
- 特殊快捷方式、便携工具和开发辅助程序可能需要右键手动修正分类；
- `.bat`、`.cmd` 等脚本在启动前会显示确认提示。

## 贡献

欢迎提交 Issue 和 Pull Request。提交代码前请运行：

```powershell
dotnet build .\ProgramDock.sln -c Release -p:Platform=x64
```

详细流程见 [CONTRIBUTING.md](CONTRIBUTING.md)。安全问题请勿公开披露，详情见 [SECURITY.md](SECURITY.md)。

## 许可证

ProgramDock 采用 [MIT License](LICENSE) 发布。
