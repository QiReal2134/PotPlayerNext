# PotPlayerNext

**作者 qireal** · Rust + WinUI 3 图片查看器 / 视频播放器

[GitHub Releases](https://github.com/QiReal2134/PotPlayerNext/releases) · [自动构建状态](https://github.com/QiReal2134/PotPlayerNext/actions/workflows/build.yml) · [发布流程](docs/RELEASING.md)

Rust 核心 + WinUI 3 图片查看器 / 视频播放器。原生 Windows 桌面应用，不使用 Electron、WebView 或 REST 服务。

> 状态：0.2.6 已发布，0.2.7 为本地修复候选（缩略图取消隔离、文件探测竞态、首次快捷预览关闭）。Rust 测试、Clippy、真实 C#↔Rust ABI、42 项生产逻辑检查通过；正常打开/快捷预览分路、首次键盘焦点及后台生命周期已有实际进程证据。资源管理器物理 Space/Esc、设置UI重启、整机性能和干净机器部署仍未完成验收。

系统媒体测试覆盖 Windows 图片解码、图片/视频缩略图、H.264 MP4 打开、播放/暂停、前后 1.5 秒跳转、2x 倍速恢复与音量；它不替代 WinUI 物理长按、资源管理器集成与性能验收。各层证据见 `docs/VALIDATION.md`。

## 来源与定位

- 参考 PotPlayer 的媒体播放交互和 ImageGlass 的轻量图片浏览体验。
- **不是 PotPlayer / ImageGlass 源码分支。** 未获得 PotPlayer 官方公开源码，不打包其二进制，不宣称采用其播放内核。
- [ImageGlass 源码](https://github.com/d2phap/ImageGlass)以 [GPLv3](https://github.com/d2phap/ImageGlass/blob/develop/LICENSE) 发布；当前没有复制其实现。为未来兼容整合，本项目原创源码同样采用 GPL-3.0-only。
- 视频使用 Windows `MediaPlayer` / WinUI `MediaPlayerElement`。并非所有 PotPlayer 支持的格式都能播放；MKV、HEVC、AV1 等取决于文件编码和已安装系统解码器。
- “外部播放器…”可手动选择已安装的 PotPlayer 或 ImageGlass EXE，将当前文件以独立参数传递。外部软件由用户安装，许可证独立。

## 已实现代码

- Rust 非递归目录扫描、图片/视频类型识别、UTF-8 C ABI、显式释放返回内存；单文件探测接口让资源管理器预览不必扫描父目录。
- 打开文件/文件夹、拖放、搜索、类型过滤、双击打开。
- 虚拟化列表：仅为已实现的容器请求缩略图，四路并发，128 项 LRU 缓存，192px 解码。
- 预览窗口：纯媒体无标题栏、无按钮、无系统描边、无常驻文字，打开时在所在显示器工作区居中；图片首次按原图比例设客户端尺寸，避免固定比例留边；四边/四角可拉伸，图片和视频按比例适应新尺寸。
- 图片和视频均支持滚轮或 `+ / -` 放大/缩小（0.1–8 倍）；仅放大且内容超出窗口时，左键拖动媒体平移查看。非媒体空白区域拖动移动窗口，右键“适应窗口”恢复完整显示。解码长边上限 2560px，**不是原尺寸像素编辑器**。
- 原生视频播放，预览默认隐藏进度条和所有播放控件；可从主窗口播放设置或预览右键菜单开启。
- `→ / D` 轻按快进、`← / A` 倒退，默认 1.5 秒（主窗口播放设置可改）；长按 `→ / D` 超过 250ms 临时 2 倍速，松开、失焦或关闭恢复原速，默认启用。轻按前进在松开时执行，长按不额外跳转。
- 快捷预览 Space / Esc 关闭；正常视频窗口 Space 暂停/继续，Esc 关闭，F 全屏；返回窗口模式后保留边缘拉伸。
- WinUI 3 原生 `DesktopAcrylicBackdrop` 毛玻璃，深/浅色跟随系统并响应颜色设置变化。透明效果被系统关闭时使用系统降级效果。
- 显式 PerMonitorV2 DPI 感知、布局像素对齐及 Segoe UI Variable 字体资源，避免系统对整个界面做位图缩放。
- 原生入口/媒体过渡动画、系统主题资源、可访问控件、主窗口 InfoBar 错误提示；多分辨率应用图标。
- qireal 天蓝圆盘 Logo：白色播放三角形与右下标 N。主界面启动时使用约 0.85 秒原生 WinUI 开屏动画，跟随系统深浅色；系统关闭动画时跳过。文件直开、空格预览和后台组件不显示开屏，不增加该动画等待。
- 正常打开与快捷预览分离：文件关联/命令行文件参数直接创建媒体窗口，不创建媒体库主界面、不扫描父目录。正常窗口有标题栏/任务栏入口和视频播放控件；`--preview -- "文件路径"` 和列表 Space 使用纯媒体置顶预览，不出现在任务栏/Alt-Tab。中文、空格、百分号与 `&` 不经过二次拆分。
- 应用内选中文件按 Space 预览，图片再次 Space 关闭。
- 资源管理器空格预览桥接（**实验性，默认启用**）：独立 `--background` 单例隐藏组件，关闭主界面仍可工作；MSI 注册当前用户登录启动并在安装完成后启动。设置可关闭并持久化；不需要先开主界面。事件驱动、不轮询按键、不记录按键。仅在本组件预览打开/加载中，且前台为该预览或其来源资源管理器时消费 Space/Esc 关闭键及其重复/释放事件，避免关闭后立即重开。地址栏/重命名等已知文本控件及修饰键会跳过；Windows 11 标签页和实际空格链路尚需实窗验收。
- 步长/长按倍速/预览控件/后台开关写入 `%LOCALAPPDATA%\PotPlayerNext\settings.json`；跨进程互斥的原子更新避免普通窗口、预览和主界面互相覆盖偏好，打开设置时重新载入。
- 原生扫描上限 20,000 项，达到后明确提示截断；不递归扫描磁盘。缩略图不支持时显示占位符。

## 安装与设为默认应用

GitHub 自动发布：推送与项目版本一致的 `vX.Y.Z` 标签，Windows 构建与检查成功后自动发布 MSI、便携版、GPL 源码和 SHA256；普通提交仅生成 CI 产物。操作见 [发布说明](docs/RELEASING.md)。

当前候选包：`artifacts/installer/PotPlayerNext-0.2.6-win-x64.msi`。简体中文安装向导支持选择目录，默认安装到当前用户的 `%LOCALAPPDATA%\Programs\PotPlayerNext`；提供开始菜单入口、修复/卸载、升级与 20 种文件关联注册，不要求管理员安装。0.2.4 后台部署已通过离线MSI检查，尚未在当前用户安装上做升级/卸载实测；0.2.2/0.2.3历史维护测试不能替代新包验收。

安装后在播放器的播放设置中点击“设为默认图片 / 视频应用”，或在 Windows 设置 → 应用 → 默认应用中找到 PotPlayerNext 并选择文件类型。安装器只注册处理能力，不覆盖现有 `UserChoice`；选择默认处理程序由 Windows 设置完成，遵循[微软默认应用注册规则](https://learn.microsoft.com/en-us/windows/win32/shell/default-programs)。编解码格式仍受系统支持限制。

构建安装包与配套 GPL 源码归档：

```powershell
./scripts/build-installer.ps1
./scripts/test-installer.ps1 -TargetDirectory artifacts/installer-test/installed-app-new
./scripts/test-upgrade.ps1
```

WiX 6.0.2 构建工具安装到项目 `.tools/wix`，扩展版本固定。MSI 包含自包含 .NET / Windows App SDK、Rust DLL、PRI/XBF 和许可说明，旁边生成 SHA256 和完整应用/安装器源码 ZIP。当前包未进行发行证书签名。

安装测试仅使用项目内独立目标目录和 HKCU 关联，校验安装、实际注册 ProgID 启动、卸载，且不改变既有默认应用。该测试不替代真实 Windows 默认应用选择与双击的人工/实窗验收。

## 开发构建

要求 Windows 10 2004+ / Windows 11 x64：

1. 安装 Rust stable MSVC 工具链（`rustup`）。
2. 安装 .NET 8 SDK。
3. 安装 Visual Studio 2022 Build Tools 的 **Desktop development with C++**，包含 MSVC x64/x86 工具和 Windows SDK。

管理员或系统要求的安装授权由安装器处理。可手动执行：

```powershell
winget install --id Microsoft.VisualStudio.2022.BuildTools --exact --override "--wait --passive --add Microsoft.VisualStudio.Workload.VCTools --includeRecommended"
```

在项目根目录运行：

```powershell
./scripts/build.ps1 -Publish
./artifacts/app/PotPlayerNext.exe
```

脚本自动发现 MSVC 环境，运行 Rust 格式检查、测试、Clippy、release DLL 编译、真实 C#↔Rust ABI 集成测试以及 WinUI 发布。首次需要联网还原依赖。发布目录包含 Windows App SDK 和 .NET 运行时，不需要单独安装运行时。发布大小不等同于运行时内存占用。

`.tools/dotnet` 中的本地 SDK（若存在）作为 PATH 中 SDK 的后备。不要提交 `.tools`、构建产物或外部媒体。

只检查 WinUI 源码、不生成 Rust DLL：

```powershell
./.tools/dotnet/dotnet.exe build ./src/PotPlayerNext/PotPlayerNext.csproj -c Release -p:Platform=x64 -p:SkipNativeCore=true
```

这个选项仅用于编译诊断，不是可运行发布版本。

独立验证系统媒体能力（不需要 MSVC，不使用 Rust DLL）：

```powershell
./scripts/test-media.ps1
```

测试生成本地 BMP 和 3 秒 H.264 MP4（无网络媒体），检查图片解码、两类缩略图、视频打开/播放/暂停/跳转/2x 倍速恢复/音量，将结果写入 `artifacts/media-smoke/results.json`。它不是整机端到端测试。

## 结构

```text
native/media-core/           Rust 扫描和 C ABI
src/PotPlayerNext/           WinUI 3 XAML + C# 薄 UI/WinRT 适配层
scripts/build.ps1           构建、测试和发布
docs/ACCEPTANCE.md          验收与性能测量步骤
.github/workflows/build.yml Windows CI 构建
```

WinUI 的 XAML / WinRT 适配用 C#，核心用 Rust；**不是纯 Rust UI**。目前 Rust 负责目录扫描，不负责系统图片/视频解码。没有服务器、监听端口或媒体上传。

## 后续

参见验收表。尚未实现 ImageGlass 多格式解码模块、FFmpeg/libmpv 后端、HDR/字幕/音轨选择、自然排序、原尺寸图片模式、文件监控、托盘常驻和自动更新。高性能低占用是设计目标，不是未经测量的承诺。
