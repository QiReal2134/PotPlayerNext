# 本地验证记录

日期：2026-10-05（Asia/Shanghai）。

## 已通过

- Microsoft 签名的官方 VS Build Tools 安装器校验通过，用户再次明确请求自动安装后执行成功，退出码 0。Visual Studio Build Tools 2022 17.14.41 / MSVC 14.44.35207 / Windows SDK 10.0.26100 已安装。
- `scripts/build.ps1 -Publish`：Rust 七项单元测试全部通过，Clippy 无警告，Rust release DLL 编译成功，WinUI Release 发布成功。之后补充真实 ABI 集成步骤。
- 真实 release DLL + 生产 C# `NativeCatalog` 集成：Unicode 文件路径、类型过滤、非递归扫描、单文件探测、错误 JSON、1,000 次内存分配/释放、20,001 文件截断与超出列表上限的单文件探测全部通过。时间 2026-10-05 02:21:45（Asia/Shanghai）；一次采样 20,001 文件扫描（含 JSON/跨 ABI）110ms，1,000 次探测 70ms。元数据用空文件，不代表实际媒体解码或 UI 性能。
- 早期发布仅验证了进程存活 5 秒；随后发现遗漏应用 PRI/XBF 导致延迟崩溃，因此该短时检查不算启动验收。现已修复发布资源复制，并在构建脚本中检查必需资源。
- 加入 ABI 测试后的 `scripts/build.ps1 -Publish` 再次全量通过，时间 02:23:18；本轮 20,001 文件扫描 98ms、1,000 次探测 66ms。已保存首轮证据到 `docs/evidence/native-smoke-20261005.json`，每轮报告另外保留在 `artifacts/native-smoke/`。
- 项目内 .NET SDK 8.0.425 安装成功。
- Rust `cargo fmt --all -- --check`：通过。
- WinUI / C# / XAML Release 编译（`SkipNativeCore=true`）：0 错误、0 警告。
- 早期 UI 壳的 5 秒存活/关闭检查不足以证明界面可用，已由后续独立窗口实际渲染检查补充。
- 烟雾测试中一次采样的 Working Set 为 118.7 MiB；这不是 Private Working Set，不是稳定空闲基准，不代表高性能目标已验收。
- 新增单文件 Rust 探测接口，资源管理器预览不再枚举父目录；对应调用编译通过，Rust 单文件探测已通过真实 ABI 测试。资源管理器全局交互仍待验收。
- 独立系统媒体测试 `tests/MediaSmoke`：320×180 BMP 解码通过；图片和视频的 Windows 缩略图均返回 100,758 字节；由本地图片生成的 640×480 H.264 MP4（3.0185714 秒）打开、播放时钟推进、暂停、跳转和音量检查均通过。生成耗时一次采样 580ms，不是播放器性能基准。测试时间 2026-10-05 02:12:08（Asia/Shanghai）。原始 JSON 位于 `artifacts/media-smoke/results.json`。
- 随后通过 `scripts/test-media.ps1` 再次执行上述五项检查，全部通过；本次 fixture 生成耗时 614ms，时间 02:13:38。`results.json` 是最新报告，新版测试另保存每轮 `results-<run>.json`，后续运行不会覆盖每轮证据。

## 历史失败（已解决）

- `cargo test`：构建依赖的 build script 时失败，`linker link.exe not found`。这是工具链缺失，不是单元测试断言失败。
- 首次用户批准 Build Tools 安装后，注册执行工具拒绝了安装调用。再次明确请求后同一注册执行工具安装成功，没有转用其他渠道绕过拒绝。
- 完整 WinUI 构建：首轮到复制 Rust DLL 时失败，`MSB3030`，不存在 `target/release/potplayer_next_core.dll`。
- Rust 工具链最初未安装 Clippy；已通过官方 `rustup component add clippy` 补齐并通过 lint。
- WinUI 发布遗漏自己的 PRI/XBF：补充发布目标，保留 XBF 相对路径，发布资源检查和实际窗口渲染通过。

## 本轮界面与快捷键改进

- `artifacts/fluent-app` 独立测试窗口实际显示：WinUI 主窗口随系统浅色主题，Acrylic 毛玻璃、四项图片/视频缩略图正常；图片和视频预览均无标题栏、按钮、文字和默认进度条，Esc 关闭通过。
- EXE 嵌入 manifest 已核对 `PerMonitorV2` / `true/pm` DPI 配置。跨显示器缩放与深浅色实时切换尚未实测。
- 生产缓存、解码尺寸、按键状态机和跳转边界的 17 项逻辑检查通过。长按阈值 250ms；松开、失焦或关闭恢复原倍速；短按默认跳转 1.5 秒。设置可保存到本地 JSON。
- `scripts/test-media.ps1` 于 02:57:06 再次通过真实 Windows 媒体引擎测试，增加 2x 倍速及恢复、前后 1.5 秒跳转检查。物理长按到界面事件的完整链路未自动实测，不以状态机/引擎测试替代。
- 最终 `scripts/build.ps1 -Publish -OutputDirectory artifacts/fluent-release` 全量通过：Rust 7 项测试、Clippy、真实 ABI、17 项生产逻辑检查、WinUI Release 发布与 PRI/XBF/DLL 必需资源检查。ABI 本轮 20,001 文件扫描 95ms（仅元数据测试）。开启进度条时禁用预览背景拖动，避免干扰播放控件。

## 预览缩放、平移与窗口拉伸增量

- 图片/视频共用 WinUI ScrollViewer 和保持宽高比的媒体区域；滚轮、加减键缩放，超出视口才启用鼠标平移，媒体之外空白区移动窗口。
- 初始窗口按显示器 WorkArea 居中；保留无标题栏外观、系统边框，并补充客户区 6 DIP 四边/四角拉伸与方向光标，最小尺寸 240×160 DIP。退出全屏重新启用拉伸。
- 首轮系统消息移动有延迟、不稳定，替换为指针捕获 + AppWindow.Move。修正版实际图片窗口：加减键放大/缩小通过；空白区拖动后截图原点从 (488,190) 变为 (613,203)，尺寸保持 755×506。首轮图片滚轮缩放与放大后平移截图确认通过。
- 最终 `artifacts/preview-interaction-v3` 全量编译发布和资源检查通过；生产逻辑扩至 22 项，新增宽高比、溢出平移门槛、多显示器负坐标居中、四角识别与拉伸最小尺寸检查，全部通过。初次几何测试使用浮点精确比较失败，改用 1e-6 容差后通过。
- 最终四边/四角拉伸及视频缩放/平移的物理交互尚未实窗确认：自动化辅助短时报 active request，恢复后截图显示其他前台界面覆盖目标窗口，未继续对覆盖界面发送输入。逻辑/编译通过不替代这些实窗验收。

## 其余尚未验收

## 安装器与默认应用打开链路增量

- 新增简体中文、当前用户 MSI 安装器：固定 WiX 6.0.2 + UI 扩展，嵌入 CAB、自包含运行时、Rust DLL、PRI/XBF、完整 GPL 和依赖许可文件；开始菜单、可选安装路径、修复/卸载和 MajorUpgrade 定义。源码 ZIP 与 SHA256 随包生成。
- 入口读取 Windows 已解析的命令行参数，支持 `--` 分隔、Unicode/空格/百分号/`&` 路径，不再忽略外部文件。先探测指定文件，超过目录扫描上限也能添加并预览；父目录扫描失败时仍可预览已成功探测的文件。
- 当前用户注册 RegisteredApplications / Capabilities / 图片与视频 ProgID / 20 个 OpenWithProgids；不写 UserChoice。主窗口提供官方 `ms-settings:defaultapps?registeredAppUser=PotPlayerNext` 入口。
- 24 项生产逻辑检查通过；新增参数解析与特殊字符路径检查。自包含应用通过真实安装后的四项启动集成测试：图片和视频分别经独立参数启动与 ShellExecuteEx 显式注册 ProgID 启动，记录正确文件哈希、预览窗口激活、真实图片解码/视频打开。测试不会改变默认选择，不能替代 UserChoice 双击验收。
- 首次 MSI 安装退出 0，20 项关联正常且默认选择未变；首次卸载退出 0 但残留文件，被测试明确判失败。原因是自定义安装目录在维护模式未持久化；已加入 InstallFolder 注册记录与搜索。修复后重复安装/卸载均退出 0，确认安装目标目录消失、全部文件和本应用关联清理，原有默认选择未变。
- 独立测试证据位于 `artifacts/installer-test/install-results.json`、`uninstall-results.json` 和 `artifacts/shell-launch/results.json`；最终交付包每次重新生成后重新测试，以 MSI SHA256 绑定报告。
- 最终 MSI（550 个载荷文件含 41 项许可文件）于 06:49–06:50 通过实际安装、`/fa` 强制修复、四项图片/视频直接/显式 ProgID 启动以及完整卸载检查，退出码均为 0。MSI SHA256：`CF19360D7EB7750D3BE21ACBE2CBB7DAD76B1229D809587B5B43FF3C662B35DD`，产品代码 `{8E660D4D-107E-44BC-8714-3CEB36A24C11}`。卸载目标不存在，全部载荷、Capabilities、两个 ProgID、Applications 条目及 20 个 OpenWith 项清理，原默认选择保持不变。
- 中间一次 shell 集成失败来自测试读写 JSONL 文件的共享模式竞争，而非播放错误；改为 FileShare.ReadWrite 读取后重跑全部四项通过，失败没有被算作成功。
- 许可文件作为普通 None 复制项发布，不加入 MRT 图片资源索引；修复其带版本文件名产生的 PRI249 qualifier 警告，随后 Release publish 无该警告。
- 主窗口/媒体加入 WinUI EntranceThemeTransition、原生 InfoBar，应用图标由项目矢量图形构建多分辨率 ICO。最终主题、动画、视频缩放与四角拉伸实窗效果仍需要完整验收，不以编译通过作视觉证明。

## 未完成的交付审计

## 0.2.1 实窗与升级增量

- 指针捕获丢失时清空平移/移动/拉伸状态，避免后续媒体拖动继承旧窗口模式。
- 实际视频预览：加号放大产生裁切，放大后拖动改变可见画面且窗口位置不变；右边和底边拉伸通过。
- 首次左上角拉伸发现位移累计，40 DIP 拖动造成约 80 DIP 缩小；修复为按下事件坐标经 ClientToScreen 定位起点，移动时用当前屏幕指针坐标，避免 HWND 位置与排队的 XAML 变换反馈。仅用按下时 GetCursorPos 曾因事件排队造成零位移，该中间版本没有被当作通过。
- 修复后相同左上角动作：755×506 → 715×466，原点 (488,190) → (538,240)，125% 缩放下精确对应 40 DIP / 50 物理像素；右下角 715×466 → 654×403；空白区移动保持尺寸。F 全屏/恢复后仍无标题栏，右下角继续拉伸到 614×365。实窗元数据记录在 `docs/evidence/ui-preview-021.json`。
- `test-upgrade.ps1` 真实执行 0.2.0 → 0.2.1：新安装不传安装目录仍恢复旧自定义目录，旧产品注册移除、新 EXE 版本正确、图片/视频命令保留，四项直接/注册 ProgID 启动和媒体解码通过，既有 UserChoice 未变；测试后卸载清理。对应 0.2.1 MSI SHA256：`1B4771F5995D01C698FB4D5DF989493A45739B91FF2B8538A70D3322833F1EC9`。
- 最终 0.2.1 仍需真实默认选择后的资源管理器双击、干净机和系统深浅色切换/动画帧验收；不标记持续目标完成。

- 真实 Windows 默认应用选择后的 BMP/MP4 双击：尚未验证；已询问用户选择保留原默认手动验收或临时切换后还原，不自行覆盖现有默认值。
- 干净 Windows x64 部署、最终安装向导视觉检查：未实测；版本升级与强制修复已在最终 0.2.1 MSI 实测通过。
- 最终实窗动画质量、系统深浅色切换及全部边角/跨 DPI 覆盖仍待验证；0.2.1 视频平移和左上/右下拉伸已有上述实窗证据。
- 安装与关联代码/本机 CLI 集成已有实证，不等于整个持续目标已完成。

- WinUI 小目录缩略图及图片/视频预览显示已验证；完整格式覆盖、大目录滚动、资源管理器桥接、整机性能和泄漏验收仍未完成。
- GitHub Actions 工作流已生成，但没有推送或触发远端 CI，不声称远端通过。

## 下一步

继续按 `ACCEPTANCE.md` 做实机用户交互与性能验收。工具链安装与完整发布阻塞已经解除，不再需要用户手动安装 Build Tools。

## 最终 0.2.1 包复验与空闲采样

- 最终 MSI 与升级测试绑定同一 SHA256：`1B4771F5995D01C698FB4D5DF989493A45739B91FF2B8538A70D3322833F1EC9`。07:15 本机实际安装、强制修复、20 项关联与四项图片/视频解码启动全部通过；卸载清理通过，原有默认选择未变。完整报告保存在 `docs/evidence/*-021-20261005.json`。
- `measure-idle.ps1` 对已完成 UI 验收的 0.2.1 主窗口作只读进程计数器采样，约 60.9 秒。6 个媒体缩略图、预览关闭的热态场景，三次私有工作集均 54,116,352 字节（51.61 MiB），平均归一化 CPU 0.00%（采样期间 CPU 时间未观察到增长）。不是冷启动、多轮统计、4K 播放或泄漏基准；同机其他测试实例可能共享 DLL 页，不混淆总工作集和私有工作集。
## 0.2.1 安装器品牌与实窗检查

- 观察发现初版向导仍为 WiX 默认红色插图；新增 build-installer-art.ps1，用项目图标和自主绘制的深蓝/青色品牌面板替换，不引入第三方美术。尺寸与变量采用 [WiX 官方文档](https://docs.firegiant.com/wix/tools/wixext/wixui/)。
- 本机 125% DPI 实际检查新版欢迎、许可、目标目录、取消终止页：品牌图形无默认 WiX 红图，许可文本显示正常；接受条款前 Next 禁用、接受后启用；默认目录为当前用户 LocalAppData Programs；取消后明确显示系统未修改，Finish 后向导窗口消失。没有通过 UI 点击安装。证据：docs/evidence/installer-ui-021-branded.json。
- 此次只改变安装器品牌，应用载荷保持已验收的 qa-screen-v2。重新构建的 MSI SHA256 为 931762AA90B4C6529DF85A79D2344DD0E105ACBF66D0C583CB4BBC5130696056；此前 1B4771... 对应原始品牌包，不能用其报告替代新包复验。
- 新包实际安装、强制修复、550 文件/20 项关联、四项图片视频直接/显式 ProgID 启动与真实解码、卸载清理均通过；既有 UserChoice 不变。证据：docs/evidence/*-021-branded-20261005.json。
- 安装器为 Windows Installer 原生 Win32 向导，应用 UI 为 WinUI 3。成功完成页、其他 DPI/干净机仍不算已视觉验证。
- 新品牌包再次通过 0.2.0 → 0.2.1 实际升级与卸载清理：自定义目录、20 项关联保留、旧产品移除，图片/视频四项启动和真实解码通过，既有默认值未变。报告与新 MSI 哈希一致。

## 0.2.2 筛选缩略图回归修复

- 实窗发现 0.2.1 按 video 名称筛选时计数正确，但三张缩略图持续为占位图；首次加载正常不能代替缓存/复用链路验证。
- 原实现于 ContainerContentChanging phase 0 读取模板和 Content；缓存返回同步完成时，容器可能尚未分配新项，身份检查丢弃缩略图。改用官方 phase 回调延后至下一阶段，并同时检查请求身份与 ItemFromContainer，继续取消回收容器的旧任务，防止串图。参考 [微软 WinUI 图片查看器示例](https://learn.microsoft.com/en-us/windows/apps/get-started/simple-photo-viewer-winui3)。
- 新版独立实窗复验：初始六项→video 三项→image 三项→清空六项，各阶段实际缩略图正确；播放设置浮层无观察到的裁切，未改偏好；列表 Space 打开纯图片预览，再次 Space 关闭。证据 ui-filter-022.json。小目录稳定刷新不冒充快速滚动/大目录或动画帧验收。
- 全量 Release 编译发布、Rust 七项测试/Clippy、真实 ABI 和24项生产逻辑检查通过；MSI 实际安装、强制修复、550文件/20项关联、四项图片视频解码启动与卸载清理通过。0.2.1→0.2.2升级通过，原默认选择不变。
- 最终 0.2.2 MSI SHA256：004E219A352FF269E3C137DAC7AB50B967BA31E57EC716D3EFBA8FED6A753942。报告见 docs/evidence/*-022-20261005.json。真实 UserChoice 后的双击、干净机、实时系统深浅色切换和动画质量仍未验证。

## 0.2.3 候选：空状态与中文回退 / 启动故障调查

- 字体资源增加 Microsoft YaHei UI / Segoe UI 回退，使用微软 FontFamily 官方支持的逗号链；不是安装字体或更改系统字体。实际字形选择/清晰度不能仅凭截图作定量承诺。
- 独立实窗通过区分无搜索结果和真实空文件夹，文案给出恢复操作；六项缩略图仍正常。证据 ui-empty-023.json。
- 候选 MSI 的首轮安装、修复和直接图片启动通过，但显式 ProgID 图片启动在解码前崩溃。Windows Application 1000 记录 Microsoft.UI.Xaml.dll / 0xc000027b，WER 记录 0x8000ffff。不是把测试程序的退出码读取异常误认为全部只是测试故障；真实应用崩溃保持未解决状态。
- 新增仅 PPN_TEST_EVENTS 启用时记录 WinUI 未处理异常，不吞异常、不假造成功；测试安全报告无法获取的 shell 退出码，仍判失败。新增最多10轮启动复验参数 PPN_TEST_REPETITIONS。0.2.3 暂不算通过，上一稳定包为0.2.2。
- 本机没有返回 Hyper-V VM cmdlet 或 WindowsSandbox.exe，HypervisorPresent=False；当前工具环境未发现可直接用于干净机部署验收的执行环境。未启用系统虚拟化或安全设置。

- 带诊断的新候选 MSI SHA256 C1C5BCF07843B3B408EFC4937833068DA6F65C4B3091B6A1FC05F35946E6FF8C：5轮×图片/视频×直接/显式ProgID，共20次实际启动、正确文件及真实解码通过；安装/修复/卸载清理通过。报告 install/shell-launch/uninstall-023-diagnostic-20261005.json。
- 20次成功未复现初始崩溃，不能证明根因修复。此时未再次自动操作其他独立窗口；这只是场景差异而非因果证明。0.2.3仍为调查候选，不覆盖0.2.2的交付结论；后续需复现并定位原始0x8000ffff。

## 0.2.3 并发复现与焦点时序假设

- 同一诊断候选包在独立 UI 激活、目录扫描和截图验收并发期间，于第3轮显式ProgID图片启动再次出现 Microsoft.UI.Xaml.dll / 0xc000027b；事件停在 preview-activated，未到 image-decoded，也未触发托管 ui-unhandled-error。失败报告 failure-023-concurrent.json 与事件文件 repro-023-concurrent.jsonl。卸载清理完成。
- 官方签名 ProcDump 已下载，但注册执行工具拒绝启动，未换通道绕过；原始WER目录只有Report.wer，没有保留转储，当前缺少原生故障栈。
- 微软 [焦点设计说明](https://github.com/microsoft/microsoft-ui-xaml/blob/main/docs/design-notes/focus.md) 说明 WinUI 在WM_ACTIVATE自身恢复焦点。候选减少 Activated 回调内额外同步 Focus，把失焦的媒体/指针清理排入DispatcherQueue；初次焦点仍在媒体加载流程设置。此改动为待验证假设，不宣称已定位根因。

- 移除Activated内同步Focus/延后失焦清理后的候选 MSI SHA256 1D188A7C1DAD0ADAD9A8285BEF29EBFAB37CA8EC9A5F9F4A6FE1E1DF1EE46ED4：5轮共20次直接/注册ProgID图片视频启动、预览激活及真实解码通过；此轮同时对之前同一独立主窗口做激活、路径Enter重新扫描和截图，未复现原第3轮崩溃；安装/修复/卸载通过，既有默认选择未改动。
- 对照仅证明该受测场景的新候选更稳定，不等同原生根因已证实。仍需更多并发轮次、该焦点改动后的实际键盘预览/失焦倍速回归以及升级复验，0.2.3继续候选。

## 0.2.3 焦点候选本轮验收收口

- 新增10轮共40次启动（图片/视频×直接/显式ProgID）：正确文件、预览激活、媒体解码通过，期间并行操作独立候选窗口的图片预览/缩放/关闭；MSI安装、强制修复和卸载清理通过。与前一20次合计60次，但不把累加次数当作原生根因证明。
- 实际0.2.2→0.2.3升级通过：目录保留、旧产品移除、四项图片视频启动/解码和卸载清理通过。报告绑定同一候选MSI哈希1D188A7C1DAD0ADAD9A8285BEF29EBFAB37CA8EC9A5F9F4A6FE1E1DF1EE46ED4。
- UI回归：图片Space预览/关闭和加号缩放通过；视频默认纯媒体，临时打开本窗口控件后A/D/Left/Right对应1.5秒与结尾钳制有进度数值证据。新增60秒H264样本避免3秒自然结束歧义，Space暂停后进度稳定，Space继续后时钟推进。原生UIA直接设置滑块值失败，改为可见进度条正常鼠标定位；不是应用故障，也未伪造成功。
- 精确观察/限制在ui-keys-023-deferred.json；系统媒体引擎测试（含2x恢复）随60秒样本再次通过。键盘长按的物理链路、UserChoice双击、实际系统深浅色切换、跨DPI和干净机器保持未验收。候选尚不能证明持续目标全部完成。

## 0.2.4 正常打开 / 快捷预览分路与后台候选

- 发现外部状态已变化：用户安装0.2.3，MP4 UserChoice为PotPlayerNext.Video，BMP仍是系统应用。未改默认选择，ShellExecuteEx无Class覆盖的MP4真实默认路由三次正确激活/解码，报告userchoice-video-023.json；不是Explorer物理双击证明。
- 文件参数在App入口先解析，正常文件只创建媒体窗口；无媒体库窗口/父目录扫描。正常窗口显示caption/switcher入口，视频控件默认开启；列表Space/--preview使用置顶纯媒体隐藏switcher入口。最后发布绑定DLL哈希的open-mode-024-final.json / preview-mode-024-final.json检查正确文件、解码及四项模式属性，不替代任务栏视觉观察或物理按键。
- 快捷预览Space关闭（图片/视频一致），正常视频Space暂停/继续；保留A/Left、D/Right和长按。改为[微软PreviewKeyDown隧道路由](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.uielement.previewkeydown)优先处理，避免正常播放控件和应用各处理一次；本次按键链路尚未实窗复验。
- 设置补齐后台开关和右键预览控件持久化；跨进程命名互斥+唯一临时文件+原子替换，打开主设置重新载入。27项生产逻辑检查包含旧JSON迁移、并发字段更新和临时文件清理；实际设置UI改值/重启未复验。
- 独立隐藏WinUI后台窗口，事件驱动键盘钩子，命名互斥单例、命名事件停止，无周期按键/目录轮询。隔离测试真实启动→重复实例退出→stop helper停止→重启通过，后台AppWindow IsVisible/IsShownInSwitchers均false；background-024.json。测试不注入按键、不操作Explorer、不写用户Run项，不能声称资源管理器空格链路通过。
- 用户在UI检查期间以物理Escape停止Computer Use，立即停止后续界面操作。之前正常视频窗口和纯图片框观察成功；Escape关闭验证没有完成，不把自动操作被停止视为播放器键盘故障或通过。
- 0.2.4 MSI新增后台登录启动和安装完成启动、维护停止；默认启用，可用设置关闭并保存Preferences DWORD，下一次安装尊重0。当前存在用户真实0.2.3安装，未覆盖/卸载，仅离线数据库检查新MSI。新包安装/升级/卸载实测及用户已停止的UI链路仍未完成。
- 最终本轮MSI SHA256：9F21DF144A419C794CECA2C4E357A9E68C51043C636AAE229CA827CE01A53EB9；最终DLL SHA256：5923E1BD197937917082520F394799704E450B9A7EF5367F416DC33937D326C6，与后台、正常/预览解码报告一致。installer-background-024-static.json确认Run注册、异步启动/同步停止动作、disabled DWORD归一、序列和550文件；它明确不是安装实测。

## 0.2.5 图片预览黑边修正候选

- 用户报告图片预览有黑边，但未给截图，不能确定是native边框、留白还是原图内容。针对应用可控的两处修正：快捷预览SetBorderAndTitleBar(false,false)，保留6DIP自定义拉伸命中；Win11按[微软DWMWA_BORDER_COLOR规则](https://learn.microsoft.com/zh-cn/windows/desktop/api/Dwmapi/ne-dwmapi-dwmwindowattribute)使用COLOR_NONE取消DWM描边，保留系统阴影。正常媒体窗口的caption/系统边框不变。
- 图片解码后按图片宽高比设置客户端尺寸（960×640上限、工作区90%以内），再居中；不拉伸、裁切或改原图。仅首次快捷预览自适应，用户手动拉伸后不会被强制改回；比例不同的空白仍按系统Acrylic主题显示。视频初始窗口行为不改。
- 28项生产逻辑检查通过（新增横图/方图/竖图/小工作区初始尺寸）；全量Rust/Clippy、真实ABI和WinUI自包含发布通过。真实BMP快捷预览事件：客户端960×540、图片320×180、HasBorder=false、DWM调用HRESULT=0，置顶/隐藏switcher仍通过；图片/视频正常与预览解码、隐藏后台单例/停止/重启通过。preview-mode-025.json保留实际属性和尺寸，不把这些属性视为截图无黑边的证明。
- 最终DLL SHA256：0E66FF00BD8660D748DB2E2923B4206BD4C86102B3B59D91758D90B83A11EC3E；MSI SHA256：AA9E280CC9383D1032DFC50ADB3F808054637D0D435E7049270788990ABB8C9F。550文件MSI离线注册/动作检查通过，报告installer-background-025-static.json。
- 没有恢复用户停止的Computer Use，也没有覆盖当前安装/默认应用。去边框后的实窗截图、四边拉伸及用户报告图片的实际效果待验收；如果黑边属于原图内容，本修改不删除该内容。

## 0.2.6 · qireal Logo / WinUI 开屏（2026-10-07）

- 已保存用户批准的 Logo 参考到 docs/design/approved-logo-reference.png；重建为天蓝圆盘、白色三角形、右下标 N 的 SVG / 六档 ICO / WinUI Path。应用主窗口、EXE 图标和安装器品牌图使用新版；不是把生成图的白背景或纹理带入软件。
- 原生 WinUI Storyboard 按圆盘→三角形→N 的顺序动画，只使用 Opacity / RenderTransform。设定总时长850ms，完成后打开主界面并销毁开屏窗口，释放主题监听和Storyboard。无GIF、视频、逐帧布局定时器或新增动画依赖。署名qireal位于图标下方。
- 开屏窗口无caption、不出现在switcher，采用现有DesktopAcrylicBackdrop / 系统深浅色；XAML使用DIP，Loaded后按RasterizationScale设置物理窗口尺寸并居中。系统AnimationsEnabled=false时跳过；本机设置为true，未为测试修改系统设置，关闭动画分支尚未实窗验证。
- 文件直开、--preview、--background、--stop-background分支均绕过开屏，保留直接进入媒体窗口的行为；没有增加快捷预览等待。文件模式测试新增禁止splash-started断言，后台测试亦新增无开屏/主窗口断言。
- 完整Rust7项/Clippy、Rust DLL真实ABI、28项生产逻辑检查和WinUI自包含编译通过。最终DLL SHA256：7C061043C8A2AC191EDF65311CDA88ECDF0A4A5F89FFFE1B74C104D9C1AC405E。
- 最终DLL三次真实WinUI主界面启动均完成动画并按顺序创建主窗口，主窗口存活；实际完成间隔约895ms，含Dispatcher/合成调度。splash-026.json绑定最终DLL哈希。正常/快捷两种模式各2轮图片+视频（共8次）正确文件激活/真实解码/窗口契约通过，open-mode-026.json / preview-mode-026.json。GUID隔离后台隐藏/单例/stop-helper/重启通过，background-026.json。
- Computer Use未恢复，未发UI输入或取得动画截图；测试事件只证明Storyboard完成、窗口路径与生命周期，不证明每一帧视觉质量、全部DPI/多屏或主题切换。没有覆盖用户现有安装或默认选择；新MSI的实际升级/卸载、Explorer空格和干净机部署仍待验收。
- 0.2.6 MSI生成与只读数据库检查通过：552个payload文件，包含SplashWindow.xbf及Controls/BrandLogo.xbf，后台动作顺序/偏好与0个UserChoice写入通过。MSI SHA256：96CD86F3BECE8EC95569F01E90CB2576FCA595ED88309F2624EBDB92540EBA40。此项不是最新包安装/升级实测，报告installer-background-026-static.json。

## 0.2.7 审查修复与首次快捷预览关闭候选

- 缩略图共享任务改为独立内部取消源、按消费者取消等待、最后消费者退出才取消工作；Clear/Dispose阻止旧结果回填，同路径替代请求只由自己的entry移除。注册entry先于调用provider，覆盖同步完成/失败重试；仍保留UI上下文、4路worker和128项LRU，不引入轮询或新媒体依赖。
- 文件直开/路径预览共用最新请求协调器；后发文件、文件夹切换、关闭主窗口或其他预览使旧探测无效，旧失败不覆盖当前状态。
- 后台记录来源Explorer和当前预览HWND，包含尚未完成的单文件探测。Space/Esc优先关闭本会话，而不是重新读取选中文件并替换窗口；dismiss绑定会话代次，旧排队事件不会关掉后来窗口。关闭消费其重复/释放事件，直到物理释放才允许新按键动作。其他窗口、修饰键、地址栏/重命名不消费。
- Activated/Loaded排入DispatcherQueue补齐首次焦点，不等待图片/视频解码，不在WM_ACTIVATE同步Focus；保留已有焦点。低级hook可在首次前台仍属于来源Explorer时关闭预览，避免依赖XAML焦点成功。
- 新增14项生产逻辑回归（总42），覆盖各取消/替代/同步完成、最新探测、加载中关闭、过期关闭、Space/Esc按住去重。第一次运行既有settings contention检查曾出现File.Move的UnauthorizedAccessException；有限复跑40项及随后两次42项均通过。没有将该既有间歇文件访问失败宣称已修复；保留为待观察项。
- Rust7项、fmt/Clippy、真实Rust DLL/C# ABI（含20,000项扫描上限）和WinUI自包含构建通过；最终DLL SHA256 5CDF2902C46F079619B58FDAE0D31DFA1EB1DFCB77C9C0C9E8A74DDD975E6915。
- 最终DLL正常和快捷两种模式各3轮图片/视频（共12次）实际激活、正确文件解码、原生窗口契约和keyboard focus=true通过，报告open-mode-027.json / preview-mode-027.json。后台GUID隔离隐藏/单例/停止/重启以及3次开屏生命周期通过，报告background-027.json / splash-027.json。发布脚本6项mock回归通过，无外部写入。
- Computer Use保持停止，不注入按键、不操作Explorer或用户安装，不改Run/UserChoice。焦点日志、会话/按键模型不能替代实际Explorer第一次Space/Esc关闭、按住关闭不重开或编辑控件回归；这些仍待实窗验收。0.2.7仅本地候选，未提交/推送、未覆盖v0.2.6 Release。
- 0.2.7 MSI生成与只读检查通过：552个payload文件、后台动作/偏好和0个UserChoice写入。MSI SHA256 C909BF4064DC7373450956D5FF0E0D336979BB199FC9A44197A37D96F0D370A3；installer-background-027-static.json。没有执行该包安装/升级/卸载，用户真实后台仍未更换。
