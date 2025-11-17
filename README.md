<div align="center">

<img src="Plain Craft Launcher 2/Images/icon.ico" alt="Logo" width="80" height="80">

# PCL Community Edition FrpOnline

[![Stars](https://img.shields.io/github/stars/65658dsf/PCL2-CE-FRPOnline?style=flat&logo=data:image/svg%2bxml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciIHZlcnNpb249IjEiIHdpZHRoPSIxNiIgaGVpZ2h0PSIxNiI+PHBhdGggZD0iTTggLjI1YS43NS43NSAwIDAgMSAuNjczLjQxOGwxLjg4MiAzLjgxNSA0LjIxLjYxMmEuNzUuNzUgMCAwIDEgLjQxNiAxLjI3OWwtMy4wNDYgMi45Ny43MTkgNC4xOTJhLjc1MS43NTEgMCAwIDEtMS4wODguNzkxTDggMTIuMzQ3bC0zLjc2NiAxLjk4YS43NS43NSAwIDAgMS0xLjA4OC0uNzlsLjcyLTQuMTk0TC44MTggNi4zNzRhLjc1Ljc1IDAgMCAxIC40MTYtMS4yOGw0LjIxLS42MTFMNy4zMjcuNjY4QS43NS43NSAwIDAgMSA4IC4yNVoiIGZpbGw9IiNlYWM1NGYiLz48L3N2Zz4=&logoSize=auto&label=Stars&labelColor=444444&color=eac54f)](https://github.com/65658dsf/PCL2-CE-FRPOnline/)
![GitHub Release](https://img.shields.io/github/v/release/65658dsf/PCL2-CE-FRPOnline?label=Release&logo=github)
[![Issues](https://img.shields.io/github/issues/65658dsf/PCL2-CE-FRPOnline?style=flat&label=Issues&labelColor=444444&color=1F883D&logo=github)](https://github.com/65658dsf/PCL2-CE-FRPOnline/issues)
[![Pull requests](https://img.shields.io/github/issues-pr/65658dsf/PCL2-CE-FRPOnline?style=flat&label=Pull%20requests&labelColor=444444&color=1F883D&logo=github)](https://github.com/65658dsf/PCL2-CE-FRPOnline/pulls)
![GitHub Actions Workflow Status](https://img.shields.io/github/actions/workflow/status/65658dsf/PCL2-CE-FRPOnline/build-test.yml)
![GitHub Downloads (all assets, all releases)](https://img.shields.io/github/downloads/65658dsf/PCL2-CE-FRPOnline/total)
[![哔哩哔哩](https://img.shields.io/badge/动态-BiliBili-00A4DB?style=flat&labelColor=444444&logo=bilibili)](https://space.bilibili.com/3546847192811755/dynamic) <br />

[社区版下载](https://github.com/65658dsf/PCL2-CE-FRPOnline/releases) |
[上游存储库](https://github.com/PCL-Community/PCL2-CE) |
[帮助文档库](https://github.com/PCL-Community/PCL2CEHelp)

[提交问题](https://github.com/65658dsf/PCL2-CE-FRPOnline/issues/new/choose) |
[贡献指南](https://github.com/65658dsf/PCL2-CE-FRPOnline/wiki/开发指南)

</div>

PCL2 CE FRPOnline 是基于 PCL-CE 开源代码二次开发的社区版本，包括将联机模块修改为使用FRP！

目前正在对接各个FRP厂家的API，以支持使用FRP进行联机。

社区版的版本号与主线并非严格对应关系，也请不要向官方仓库反馈社区版问题。

欢迎大家来用用看！

构建命令：

- x64： dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.vbproj" -c Release -r win-x64
- ARM64： dotnet publish "Plain Craft Launcher 2/Plain Craft Launcher 2.vbproj" -c Release -r win-arm64

### ✨ 隐藏提示

在全局配置项中添加 `UiLauncherCEHint` 字段，字段值为 `False`。

## 💻 支持平台

| 操作系统 | 支持的启动器版本 | 环境要求 | 社区技术支持 |
|---|---|---|---|
| Windows 10 1809 (17763) 或更高 | [最新版](https://github.com/65658dsf/PCL2-CE-FRPOnline/releases/latest) | [.NET 8 Desktop Runtime](http://get.dot.net/8) | ✅ 完整支持 |
| Windows 8 - Windows 10 1809- (17763-) | [最新版](https://github.com/65658dsf/PCL2-CE-FRPOnline/releases/latest) | [.NET 8 Desktop Runtime](http://get.dot.net/8) | ⚠️ 理论能跑，但不提供社区支持 |
| Windows 7 或更低版本 | [2.9.5](https://github.com/65658dsf/PCL2-CE-FRPOnline/releases/tag/2.9.5) | [.NET Framework 4.8](https://dotnet.microsoft.com/zh-cn/download/dotnet-framework/thank-you/net48-offline-installer) | ❌ 不提供社区支持 |
| macOS / Linux / 其他操作系统 | 暂不支持 | [.NET 9 SDK](http://get.dot.net/9) | ⚠️ 仅跨平台开发支持（交叉编译） |

**✅ 完整支持**：尽可能提供一切相关支持，但必须确保启动器为最新版本。

**⚠️ 理论能跑，但不提供社区支持**：PCL2 CE FRPOnline 应该可以在这些平台上运行，但不保证功能完全可用。你必须升级到完整支持的系统版本以获得社区技术支持。

**❌ 不提供社区支持**：不保证 PCL2 CE FRPOnline 在这些平台的可用性，甚至压根打不开。请升级操作系统以使用 PCL2 CE FRPOnline。

**⚠️ 仅跨平台开发支持（交叉编译）**：PCL2 CE FRPOnline 的源代码可以在 macOS 与 Linux 平台编译，但无法直接运行。作为开发者，你可以在这些平台上进行开发，然后将编译产物转移到 Windows 系统测试。

**注**：    
社区仅对最新版本的启动器提供支持。    
取决于部分问题的特殊性（如系统不完整），有时你仍然必须升级操作系统以继续获得支持。    
PCL2 CE FRPOnline 始终建议使用最新版本的操作系统以获得最佳体验。
Windows 7 仍然可以尝试使用最新版本的启动器，但可能会遇到很多额外问题。

## 🔒 许可证

- `PCL.Core/` 使用 [Apache License 2.0](https://github.com/PCL-Community/PCL.Core/blob/main/LICENSE)
- `Plain Craft Launcher 2/` 使用 [自定义许可证](./LICENCE)

## 🌟 统计数据
![Alt](https://repobeats.axiom.co/api/embed/7780da7a2612e74751bdf872f507efe2ea132b3a.svg "Repobeats analytics image")

[![Star History Chart](https://api.star-history.com/svg?repos=65658dsf/PCL2-CE-FRPOnline&type=Date)](https://www.star-history.com/#65658dsf/PCL2-CE-FRPOnline&Date)

**此页浏览量**（总计 / 今日）：[![Hits](https://hits.zkitefly.eu.org/?tag=https://github.com/65658dsf/PCL2-CE-FRPOnline)](https://hits.zkitefly.eu.org/?tag=https://github.com/65658dsf/PCL2-CE-FRPOnline&web=true)
## ❤️ 贡献者

[![](https://contrib.rocks/image?repo=65658dsf/PCL2-CE-FRPOnline)](https://github.com/65658dsf/PCL2-CE-FRPOnline/graphs/contributors)
