# LIVP Viewer

Windows 10/11 桌面 LIVP 实况照片预览器。拖入 `.livp`，或把它交给 `LivpViewer.exe` 打开。程序在内部读取 ZIP 容器中的 HEIC/JPEG 照片和 MOV/MP4 视频；用户不需要手工解压。

## 使用

- 双击 `.livp`：先运行根目录的 `build.cmd` 生成 EXE，再运行 `register.cmd` 注册当前用户的文件关联。也可右键 LIVP 文件，选择“打开方式” → `LivpViewer.exe` → “始终使用”。Windows 若保留已有默认程序，仍需在“打开方式”里确认一次。
- 拖拽 `.livp` 到窗口，或点击“打开”。
- 默认显示静态照片。按住照片播放实况，长按松开后返回照片；轻点照片后持续播放，再点视频或点击“返回照片”停止。空格键也可以切换。
- 鼠标滚轮缩放静态图，`Ctrl+0` 恢复适应窗口。
- 左右方向键或顶部箭头浏览当前文件夹的 `.livp`；`Esc` 停止播放，`Ctrl+O` 打开文件。

声音由 LibVLC 播放。HEIC 由打包的 Magick.NET 解码，MOV 由打包的 LibVLC 解码；不依赖 Microsoft Store 的付费 HEVC 扩展，也不要求单独安装 VLC。无声源视频自然不会产生声音。

## 构建

在 Windows 10/11 x64 上安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，于项目根目录执行（PowerShell 或命令提示符均可）：

```bat
build.cmd
```

生成 `dist/LivpViewer-win-x64/LivpViewer.exe`，并在系统提供 `tar` 时生成 `dist/LivpViewer-win-x64.zip`。运行 `register.cmd` 前必须先完成构建；它只登记已经生成的 EXE。解压整个目录后运行 `LivpViewer.exe`，不要只拷贝 EXE；VLC 的原生 DLL 和插件需要留在目录内。此发布包包含 .NET 运行时，无需在目标机器另外安装。构建过程需要联网恢复 NuGet 包。`.github/workflows/windows-build.yml` 可在 GitHub Actions 的 Windows runner 上构建并下载相同 ZIP。

如果双击 `build.cmd`，窗口会在完成后停住，显示结果码。构建详情保存在项目根目录的 `build.log`。若未看到 `dist/LivpViewer-win-x64/LivpViewer.exe`，打开 `build.log` 查看最后几行错误；常见原因是没有安装 .NET 8 SDK，或首次构建时无法访问 NuGet。

根目录的 `build.cmd` 和 `register.cmd` 都不运行 PowerShell 脚本，不受其脚本执行策略影响。原有 `.ps1` 文件仅作备选；遇到“禁止运行脚本”时使用 `.cmd` 即可，不需要修改系统设置。

开发运行：

```powershell
dotnet run --project src/LivpViewer/LivpViewer.csproj -- "C:\Photos\example.livp"
```

容器自检：

```powershell
dotnet run --project src/LivpViewer/LivpViewer.csproj -- --self-test
```

自检验证 ZIP 内媒体定位、解码 JPEG、临时文件隔离及缺失静态图时的报错。真实 HEIC/MOV 的解码与声音仍需在 Windows 上用实际 LIVP 文件验收。

## 实现说明与边界

- 支持容器内的 `.heic`、`.heif`、`.jpg`、`.jpeg`、`.png` 静态图和 `.mov`、`.mp4` 视频。若有多组，优先选择同名 HEIC 和视频。
- 视频只临时提取到系统临时目录；对应照片从最近 3 张缓存中淘汰或关闭窗口时尝试删除。Windows 如果仍占用文件，临时目录可能残留。
- 静态图解码后立即显示，视频提取始终在后台进行，首次点击播放时窗口仍可响应。底部状态栏显示静态图读取耗时，便于判断慢在图片解码还是视频启动。
- 默认只生成最长边 2048 像素的静态预览，滚轮放大超过 1.3 倍时才在后台读取原分辨率；放大时只绘制屏幕可见区域。
- 最近浏览的 3 张预览留在内存中，来回切换会复用；较小的视频（不超过 32 MiB）在显示照片后后台准备。缓存随窗口关闭释放，不无限增长。
- 连续按方向键会按请求顺序浏览；缓存淘汰或关闭窗口时会取消仍在后台提取的视频。
- 同一张照片多次按住/松开时复用已载入的 LibVLC 播放器与视频，优先尝试硬件解码。底部状态栏分别显示视频准备与播放启动耗时，便于定位等待来源。硬件是否支持特定 HEVC 视频由设备决定。
- 单张静态图上限 150 MiB、单段视频上限 2 GiB，避免异常压缩包耗尽磁盘。大文件切换时会等待解码完成。
- 目前提供 x64 构建。`build.ps1 -Runtime win-arm64` 可尝试 ARM64，但需相应原生依赖支持，尚未验证。
- 本项目没有针对 Windows ARM 或真实设备上的所有 LIVP 变体做兼容性验证。

## 来源与许可

本项目的代码与文档由 GPT-6 Sol 生成。项目本体采用 [MIT License](LICENSE)，允许自由使用、修改和分发。第三方组件仍适用各自的许可证与再分发要求，详见 [THIRD-PARTY.md](THIRD-PARTY.md)。
