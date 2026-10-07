# 本次工具与执行范围

GitHub发布scope：用户要求上传并配置自动发布，进一步确认新建公开PotPlayerNext仓库。preferred_tool=GitHub API/CLI；selected_tool=注册exec_command + 项目.tools/gh官方GitHub CLI 2.102.0，下载SHA256 ae64e556ecc240b200f7eba60d550e4bb60d78e860e69dd88c449405b86067f4与官方asset digest一致；capability_match=当前登录账号新仓库创建、Git推送、Actions/Release验证；risk=本项目公开源码/发布产物，OAuth由用户自己登录授权；fallback_reason=没有已连接GitHub写入MCP，CLI走官方API。只设置项目Git身份/认证helper，不改其他仓库、不提交.tools/产物/凭据、不强制覆盖远端分支或已公开版本。

GitHub gate：trigger=明确上传及自动发布请求；feedback_gate=公开仓库确认、用户登录成功、待上传源码/忽略规则审查、版本/标签匹配、CI和校验成功；exit_condition=推送并验证一次实际Actions→Release；如认证/网络阻塞保留本地配置和具体未完成项，不声称已上传/已发布。GitHub CLI首次下载为网络EOF，使用相同官方URL的有界下载重试成功，不是绕过执行拒绝。

0.2.6 Logo/开屏 scope：preferred_tool=Current AI Agent / CLI build adapter；selected_tool=注册 apply_patch / exec_command；capability_match=确定性 SVG、WinUI Path/Storyboard、ICO 生成与本工作区编译/子进程启动测试；risk=项目文件和仅测试子进程；fallback_reason=原生可用，不使用 GIF/视频或新增外部运行时。用户已批准 Logo 并明确要求接入开屏。Computer Use 保持停止，不发 UI 输入；本轮不改真实安装、Run、UserChoice 或系统动画偏好。动画测试输出存 artifacts/splash-test，发布报告绑定 DLL 哈希。

执行 gate：trigger=已批准 Logo 开屏需求；feedback_gate=资源发布完整、Storyboard 无错误且完成后主窗口存活、正常/预览/后台无开屏；exit_condition=有限三次启动、真实图片/视频两种模式和后台测试后生成候选 MSI，视觉/多 DPI 仍标待验收。测试失败只修源码并重新验证，不换通道恢复停止的 UI adapter。

| preferred_tool | selected_tool | capability_match | risk | fallback_reason |
|---|---|---|---|---|
| WebFetch | 注册的 web.run / PowerShell Invoke-RestMethod | 官方 API/许可资料、包版本、开发 SDK | 外部资料和下载包 | 本地未注册独立 WebFetch |
| Current AI Agent | 当前 Codex + apply_patch | Rust/C#/XAML 源码生成 | 文件修改，限项目目录 | 原生可用 |
| CLI build adapter | 注册的 exec_command | cargo、dotnet 编译验证 | 执行构建、还原依赖 | 原生可用 |
| Browser / native UI MCP | 注册 node_repl + computer-use skill 的 sky adapter | 独立 WinUI 测试窗口截图、控件与键盘交互 | 仅本任务测试窗口；用户明确恢复自动验收 | 浏览器工具不能操作原生 WinUI；使用已注册的原生 UI adapter |
| Installer adapter | 注册的 exec_command，用户再次明确要求自动安装 | VS Build Tools 安装 | 系统级安装，数 GB | 首次调用被拒绝；再次收到明确请求后，同一注册工具成功执行微软签名安装器，退出码 0 |
| Packaging adapter | 注册 exec_command + 项目内 WiX 6.0.2 CLI | 中文 MSI、升级/卸载、源码归档 | 本地包生成；NuGet 下载到 .tools/.wix | 无独立安装包 MCP；固定版本工具通过构建适配器 |
| Installer test adapter | 注册 exec_command + msiexec + 项目 ShellLaunch 测试 | 实际安装/HKCU 注册/显式 ProgID 启动/卸载 | 项目内专用目标目录；不写 UserChoice，不改现有默认值 | 当前环境没有独立干净 VM；本机限定范围验证，不声称干净机通过 |

Scope Gate：源代码修改限 PotPlayerNext；.NET SDK 安装于项目 `.tools`；微软官方依赖还原到正常 NuGet 缓存；Rust 构建为本地目录。未连接外部媒体源、未开启 REST 服务。构建工具系统安装获得用户明确批准，校验签名为有效 Microsoft Corporation 后成功安装。另补齐 Rust Clippy 组件。

证据：SDK 8.0.425 安装成功；cargo test 在调用 MSVC linker 时失败（link.exe not found）；首次 WinUI 编译通过代码阶段但复制 Rust DLL 失败。后续 WinUI 壳编译和短时启动验证见 VALIDATION.md，不把未执行的 CI 当作通过。

额外范围：MSI 测试只注册本应用的 HKCU Capabilities/ProgID/OpenWith，不写默认选择或其 Hash。卸载用产品 ProductCode。第一轮自定义目录卸载残留留在 `artifacts/installer-test/installed-app`；注册执行工具拒绝了清理调用，未改用其他渠道删除，后续测试使用新的空目录。依据真实残留修复安装路径持久化，再验证卸载。

只读性能 adapter：注册 exec_command + 项目 measure-idle.ps1；仅允许本工作区 PotPlayerNext 进程，读取进程时间/内存和 WMI 计数器，不发 UI 输入、不终止进程。Win32 ClientToScreen/GetCursorPos 位于应用自己的拖动实现，非工具端输入注入。输出保存 JSON 后核对场景和采样边界。

安装器品牌图形由项目自身 System.Drawing 脚本生成，不使用外部图片；按 WiX 官方文档的 WixUIDialogBmp / WixUIBannerBmp 尺寸。UI adapter 只观察/导航许可和目录页，再取消；未点击 UI 安装动作。后续安装维护测试仍通过既有 CLI adapter 和专用空目录。

故障诊断：只读 Windows Application 事件日志筛选本应用/本项目测试进程；源码仅在既有 PPN_TEST_EVENTS 显式启用时记录未处理异常，不更改系统日志/安全设置、不吞故障。候选包使用独立项目目标目录重复20次启动验收，失败与成功报告均保留。

原生转储适配器：preferred_tool=IDA MCP / native debugger；selected_tool=注册 exec_command + 官方签名 ProcDump（未执行）；capability_match=本项目独立进程异常转储；risk=仅测试进程内存；fallback_reason=无已注册 IDA/WinDbg。下载签名验证有效，但启动调用被注册执行工具拒绝，未改用其他通道启动该工具。后续只使用既有普通应用启动测试及应用自身 opt-in 事件，不能视为已取得原生栈。

0.2.4 范围：用户要求不先开主界面的空格预览，源码/新MSI注册HKCU Run后台组件及维护停止，但本轮不覆盖用户真实0.2.3安装、不执行新包安装/卸载、不改UserChoice。Scope Gate采用项目内publish目录+GUID隔离的后台mutex/事件，真实进程隐藏/单例/stop-helper/重启测试经exec adapter执行，JSON绑定DLL哈希；无键盘注入、无Explorer UI操作、不写真实Run。MSI通过WindowsInstaller只读数据库adapter检查550文件、Run值、执行顺序、DWORD关闭偏好和0个UserChoice写入。UI adapter被用户物理Esc停止后，未再调用；普通非UI构建继续，无法视为UI验收已恢复。
