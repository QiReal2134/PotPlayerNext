# 格式与解码能力

扩展清单唯一来源为 `formats/media-formats.json`，Rust识别、MSI关联与安装测试共用；源码包包含它。扩展名识别不是解码承诺。

| 类别 | 路径与条件 | 本机实际验证 |
|---|---|---|
| PNG/JPEG/BMP/TIFF/JPEG XR/ICO | Windows图像decoder，按预算缩放BGRA、EXIF方向及sRGB；无需附带第三方引擎 | PNG/JPEG/BMP/TIFF/JPEG XR实际WinUI解码；ICO未实测 |
| GIF | 原生BitmapImage路径保留动画能力；缩略图是静态首帧 | 单帧GIF实际显示；动画时序未验收 |
| SVG | WinUI secure static rendering，无脚本/交互/动画；16MiB输入上限，64KiB根头解析上限，禁止DTD/外部XML解析 | 本地320×180静态SVG实际WinUI显示及缩略图 |
| WebP/HEIF/AVIF/DDS/相机RAW | 通过已安装Windows imaging codec解码；具体变体、相机型号、OS版本可能不支持，显示首帧 | 本机缺少WebP编码器，未生成样本；HEIF/AVIF/DDS/RAW未实测，不自动安装codec |
| MP4/ASF/WMV/MKV/MOV/AVI/WebM/MPEG/TS/MTS/M2TS/3GP/3G2/VOB等 | Media Foundation媒体引擎；容器、视频与音频codec均须受系统支持；不捆绑FFmpeg | H.264 MP4及真ASF/WMV打开/解码；其他容器/codec组合未全部验证 |

SVG源尺寸缺失时使用viewBox，仍缺失时300×150回退。Raster预览长边≤2560，缩略图≤192；矢量可上采样到相同预算。此处预算为应用输出，不保证第三方decoder内部从未分配原图。

官方依据：[BitmapDecoder](https://learn.microsoft.com/en-us/uwp/api/windows.graphics.imaging.bitmapdecoder)、[WinUI SvgImageSource](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.media.imaging.svgimagesource)、[Windows媒体codec列表](https://learn.microsoft.com/en-us/windows/uwp/audio-video-camera/supported-codecs)。不添加来源不明codec包，不改变用户默认应用。

复验：`.tools/dotnet/dotnet.exe run --project tests/FormatFixtures/FormatFixtures.csproj -c Release` 生成本地真实编码样本；`scripts/test-formats.ps1` 启动本工作区候选并核对尺寸/焦点，无键盘注入。报告见 `docs/evidence/format-fixtures-030.json`、`format-launch-030.json`。
