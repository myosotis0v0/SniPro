# SniPro

轻量化的 WPF GIF 截屏工具。SniPro 常驻系统托盘，通过全局快捷键开始截取屏幕区域，录制后可以在预览窗口裁剪帧范围并保存为 GIF。

## 运行要求

- Windows 10/11 x64
- [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)

当前发布包是框架依赖版，不包含 .NET 运行时；首次运行前请安装对应的 Desktop Runtime。

## 使用方式

1. 运行 `SniPro.exe`，程序会最小化到系统托盘。
2. 使用设置中的全局快捷键打开截屏覆盖层。
3. 拖动选择区域，必要时调整四角，然后点击“开始录制”。
4. 录制过程中点击“结束录制”、按 Enter，或按 Esc 停止录制。
5. 在预览窗口调整开始帧和结束帧，点击“保存 GIF”。

保存成功后，SniPro 会把已保存 GIF 的文件引用放入系统剪切板。支持文件拖放或 GIF 粘贴的应用可以直接使用该内容；不要在粘贴前删除或移动该 GIF 文件。

## 设置项

- 输出目录
- 帧率（1–30 FPS）
- 输出缩放（25%–100%）
- 最大颜色数（32/64/128/256）
- Floyd–Steinberg 抖动
- 开机自启
- 全局截屏快捷键
- 简体中文 / English / 跟随系统

录制帧缓存有 512 MB 安全上限。大区域或长时间录制时，可以降低输出缩放或缩短录制时长。

设置文件位于 `%APPDATA%\\SniPro\\settings.json`。

## 从源码构建

```powershell
dotnet build SniPro.sln -c Release
dotnet test tests/SniPro.Tests/SniPro.Tests.csproj -c Release --no-restore
dotnet publish src/SniPro.App/SniPro.App.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o .\publish
```

发布目录中的 `SniPro.exe` 仍然需要目标机器安装 .NET 10 Desktop Runtime。
