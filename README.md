<p align="center">
  <img src="assets/banner.png" alt="ZSnaper Banner" width="100%" />
</p>

# ZSnaper

ZSnaper 是一款专为 Windows 平台设计的高性能桌面屏幕捕获与本地离线 OCR 文字识别套件。项目基于 C# 12 与 .NET 8 技术栈开发，采用 Windows 原生 WinRT 离线 OCR 引擎与 SkiaSharp 硬件加速光栅渲染层，旨在为企业办公、软件工程与数据处理等场景提供高精度、低延迟、零数据外泄的屏幕取词与图像标注解决方案。

<p align="left">
  <img src="https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-0078D6.svg" alt="Platform" />
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4.svg" alt=".NET 8" />
  <img src="https://img.shields.io/badge/Language-C%23%2012.0-239120.svg" alt="C# 12" />
  <img src="https://img.shields.io/badge/Graphics-SkiaSharp%202.88-EA580C.svg" alt="SkiaSharp" />
  <img src="https://img.shields.io/badge/OCR-Windows.Media.Ocr%20(Local)-10B981.svg" alt="OCR" />
  <img src="https://img.shields.io/badge/DPI-Per--Monitor%20V2-8B5CF6.svg" alt="High DPI" />
  <img src="https://img.shields.io/badge/License-MIT-64748B.svg" alt="License" />
  <img src="https://img.shields.io/badge/Privacy-100%25%20On--Device-success.svg" alt="Privacy" />
</p>

---

## 目录

- [核心设计原则](#核心设计原则)
- [核心功能特性](#核心功能特性)
  - [1. 屏幕捕获与智能选取](#1-屏幕捕获与智能选取)
  - [2. 图像标注与样式系统](#2-图像标注与样式系统)
  - [3. 本地原生 OCR 文字识别](#3-本地原生-ocr-文字识别)
  - [4. 工作流与自动化输出](#4-工作流与自动化输出)
  - [5. 视觉交互与主题架构](#5-视觉交互与主题架构)
- [系统架构与技术选型](#系统架构与技术选型)
- [系统环境要求](#系统环境要求)
- [编译与部署指南](#编译与部署指南)
  - [开发环境准备](#开发环境准备)
  - [源码编译](#源码编译)
  - [独立单文件发布](#独立单文件发布)
- [默认快捷键规范](#默认快捷键规范)
- [配置参数说明](#配置参数说明)
- [数据安全与隐私声明](#数据安全与隐私声明)
- [开源协议与第三方依赖](#开源协议与第三方依赖)

---

## 核心设计原则

- **数据隐私合规 (Zero Data Leakage)**: 所有的图像捕获、像素采样、图形批注以及 OCR 文本识别均完全在本地终端的 CPU/GPU 与内存中执行，不包含任何遥测采集、网络请求或云端上传逻辑。
- **低延迟响应 (Low Latency Execution)**: 采用轻量化 Win32 窗口消息循环机制，捕获覆盖层与工具栏响应时间控制在毫秒级别。
- **高分屏混合缩放适配 (Per-Monitor V2 DPI Awareness)**: 完整支持多显示器、多分辨率以及不同 DPI 缩放比率环境下的像素对齐与无损显示。
- **模块化与低资源占用 (Modular & Lightweight)**: 后台常驻托盘模式下仅维持基础热键监听，空闲状态内存占用保持在极低水平。

---

## 核心功能特性

### 1. 屏幕捕获与智能选取

- **虚拟屏幕跨屏捕获**: 基于 Windows 虚拟屏幕坐标系 (`SystemInformation.VirtualScreen`)，无缝支持多显示器排列布局。
- **双层智能元素探测**: 
  - 第一层：通过 Win32 API (`ChildWindowFromPointEx` 等) 实现快速窗口与句柄探测；
  - 第二层：通过 Windows UIAutomation 结构树实现深度控件级边界分析与精准吸附。
- **鼠标指针动态合成**: 支持在捕获瞬间记录系统鼠标指针形态与热点坐标，按需合成至导出图像中。
- **微调与自由变换**: 支持捕获选区的 8 方向锚点缩放、边缘拖拽微调及全选区移动。

### 2. 图像标注与样式系统

- **矢量标注工具链**:
  - **画笔工具**: 支持自定义笔触粗细与颜色，提供平滑插值路径绘制。
  - **几何箭头**: 支持开放式 (Open)、填充式 (Filled) 及双向 (Double) 箭头形态。
  - **富文本标注**: 内置富文本编辑框，支持实时修改字体系列、字号大小及颜色样式。
  - **像素级马赛克**: 提供高斯混淆与网格像素化处理，支持笔刷尺寸与像素粒度无级调节。
- **撤销与重置栈**: 完整的历史操作栈管理，支持多级回退与画布选区重置。
- **动态样式面板**: 浮动式属性调节栏，集成 Hex 色值拾取器与实时数值调节滑块。

### 3. 本地原生 OCR 文字识别

- **原生 WinRT 引擎**: 深度集成 `Windows.Media.Ocr` API，直接调用操作系统底层离线文字识别能力，无需外部依赖库或第三方服务授权。
- **图像预处理增强管线 (`OcrImagePreprocessor`)**: 对目标选区进行灰度转换、动态对比度拉伸及二值化处理，显著提升低对比度或暗色背景文本的识别准确率。
- **排版结构化清洗 (`OcrTextFormatter`)**: 自动合并中英文字符间异常断行，智能去除无意义空白符，保留代码与列表格式。
- **独立悬浮结果面板 (`ResultForm`)**:
  - 字符与词数实时统计；
  - 识别结果编辑与单行/多行二次整理；
  - 一键复制到系统剪贴板。

### 4. 工作流与自动化输出

- **完成行为策略 (`ConfirmButtonBehavior`)**: 支持自定义确认按钮行为（复制到剪贴板、保存到文件、复制并保存、仅完成）。
- **工具栏布局模式 (`CaptureToolbarLayout`)**:
  - **Minimal (精简模式)**: 仅保留核心标注与完成操作；
  - **Annotation (标注模式)**: 面向图像编辑与说明场景；
  - **Recognition (识别模式)**: 面向取词与数据录入场景；
  - **Full (完整模式)**: 展示全功能工具链；
  - **Custom (自定义模式)**: 自由调整工具项顺序与可见性。
- **自动工具栏停靠与避让**: 智能计算屏幕边界与选区遮挡关系，自动选择最佳展示坐标。

### 5. 视觉交互与主题架构

- **主题模式切换**: 原生支持浅色模式 (Light)、深色模式 (Dark) 及跟随系统主题 (System)。
- **动效分级控制 (`AnimationLevel`)**:
  - Fast (精简快速，约 100ms)；
  - Balanced (默认均衡，约 200ms)；
  - Elegant (流体平滑，约 320ms)。
- **硬件级光栅图层**: 基于 SkiaSharp 2.88 实现自定义光晕、圆角与复杂阴影光栅化，保证 UI 交互的高帧率与低 CPU 负载。

---

## 系统架构与技术选型

```
+-----------------------------------------------------------------------+
|                              ZSnaper App                              |
+-----------------------------------------------------------------------+
|  Presentation Layer                                                   |
|  - MainForm (Settings / Control Center)                               |
|  - OverlayForm (Full-Screen Capture & Interactive Canvas)             |
|  - ResultForm (Floating OCR Preview & Text Workbench)                |
|  - Modern Controls (SkiaSharp Raster Layer / Lucide Vector Icons)     |
+-----------------------------------------------------------------------+
|  Core Services Layer                                                  |
|  - CaptureService        : Virtual Screen Bitmap & Cursor Blending    |
|  - SmartSelectionService : Win32 & UIAutomation Element Hit Testing   |
|  - OcrService            : Windows.Media.Ocr Pipeline & Preprocessing |
|  - HotkeyService         : Low-Level Global Keyboard Hook Management  |
|  - ConfigService         : JSON Schema Persistence (%APPDATA%)        |
+-----------------------------------------------------------------------+
|  Platform & Runtime Interop                                           |
|  - Microsoft.NET.Sdk (.NET 8.0 Windows Desktop)                       |
|  - Win32 API Interop (user32.dll, gdi32.dll, dwmapi.dll)              |
|  - WinRT Runtime (Windows.Graphics.Imaging, Windows.Media.Ocr)        |
|  - SkiaSharp 2.88.9 Native Engine                                     |
+-----------------------------------------------------------------------+
```

---

## 系统环境要求

| 项目 | 要求规格 | 补充说明 |
| :--- | :--- | :--- |
| **操作系统** | Windows 10 (Version 1809 / Build 17763.0) 或更高版本；Windows 11 | 支持 64 位 (x64) 与 ARM64 架构 |
| **运行库** | .NET Desktop Runtime 8.0 | 若使用独立发布包 (Self-Contained) 则无需单独安装 |
| **语言包支持** | Windows 系统已安装目标识别语言包 | 在「Windows 设置 > 时间和语言 > 语言和区域」中配置 |
| **显示子系统** | 支持 Direct3D / GDI+ 硬件加速的显示适配器 | 兼容多显示器混合 DPI 环境 |

---

## 编译与部署指南

### 开发环境准备

1. 安装 [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (8.0.100 或更高版本)。
2. 安装 [Visual Studio 2022](https://visualstudio.microsoft.com/) (17.8+，需勾选「.NET 桌面开发」工作负载) 或 Visual Studio Code (配合 C# Dev Kit)。

### 源码编译

```powershell
# 1. 克隆代码仓库
git clone https://github.com/YourOrg/ZSnaper.git
cd ZSnaper

# 2. 还原 NuGet 依赖包
dotnet restore

# 3. 编译 Debug 版本
dotnet build -c Debug

# 4. 编译 Release 版本
dotnet build -c Release
```

### 独立单文件发布

如需生成无需安装 .NET 运行库的绿色便携式二进制分发包，可执行以下命令：

```powershell
dotnet publish -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:EnableCompressionInSingleFile=true `
  -o ./publish/win-x64
```

发布输出目录位于 `./publish/win-x64/ZSnaper.exe`。

---

## 默认快捷键规范

| 快捷键 | 作用域 | 功能描述 |
| :--- | :--- | :--- |
| `Alt + Q` | 全局 | 启动区域屏幕捕获 |
| `Alt + X` | 全局 | 启动区域截图标注并直接触发 OCR 识别 |
| `Enter` | 捕获覆盖层 | 按照预设策略确认并完成截图 |
| `Ctrl + C` | 捕获覆盖层 / OCR 窗口 | 复制当前选区图像 / 复制 OCR 解析文本至剪贴板 |
| `Ctrl + S` | 捕获覆盖层 | 保存当前选区图像至配置的目标存储路径 |
| `Ctrl + Z` | 捕获覆盖层 | 撤销上一步标注操作 |
| `Esc` | 捕获覆盖层 / OCR 窗口 | 取消当前捕获 / 关闭悬浮结果窗口 |
| `~` (波浪键) | 捕获覆盖层 | 切换是否在最终截图中包含系统鼠标光标 |
| `方向键` | 捕获覆盖层 | 对当前选区位置进行像素级微移 |

*注：全局快捷键可在设置界面中进行自定义录制与冲突重置。*

---

## 配置参数说明

ZSnaper 的运行时配置存储于用户应用数据目录中：`%APPDATA%\ZSnaper\config.json`。

### 配置文件结构示例

```json
{
  "Theme": 0,
  "AnimationMode": 1,
  "EnableGlowEffect": true,
  "AccentColorHex": "#10B981",
  "AutoCopyClipboard": true,
  "AutoSavePictures": true,
  "AutoCleanOcrParagraphs": true,
  "ShowNotification": true,
  "ToolbarPlacement": 0,
  "ConfirmButtonBehavior": 0,
  "AnnotationToolBehavior": 0,
  "AnnotationColorHex": "#FF3B30",
  "AnnotationFontFamily": "Microsoft YaHei UI",
  "AnnotationFontSize": 18.0,
  "AnnotationPenWidth": 4.0,
  "AnnotationMosaicSize": 24.0,
  "AnnotationMosaicPixelSize": 10,
  "CustomSavePath": "",
  "AutoStartOnBoot": false,
  "CaptureHotkey": "Alt+Q",
  "OcrHotkey": "Alt+X"
}
```

### 字段说明表

| 字段名称 | 数据类型 | 默认值 | 描述 |
| :--- | :--- | :--- | :--- |
| `Theme` | Integer | `0` | 主题模式：`0` (Light), `1` (Dark), `2` (System) |
| `AnimationMode` | Integer | `1` | 动画模式：`0` (Fast), `1` (Balanced), `2` (Elegant) |
| `EnableGlowEffect` | Boolean | `true` | 是否启用 UI 边缘发光光栅特效 |
| `AccentColorHex` | String | `"#10B981"` | 应用程序主强调色（十六进制 RGB） |
| `AutoCopyClipboard` | Boolean | `true` | 截图完成后是否自动写入系统剪贴板 |
| `AutoSavePictures` | Boolean | `true` | 截图完成后是否自动保存至本地图片目录 |
| `AutoCleanOcrParagraphs` | Boolean | `true` | OCR 解析后是否自动进行中英文段落排版清洗 |
| `ToolbarPlacement` | Integer | `0` | 工具栏定位策略：`0` (Auto 智能避让), `1` (Top), `2` (Bottom), `3` (Inside) |
| `ConfirmButtonBehavior` | Integer | `0` | 确认按钮行为：`0` (Copy), `1` (Save), `2` (CopyAndSave), `3` (FinishOnly) |
| `CustomSavePath` | String | `""` | 自定义保存目录路径（留空则默认保存至 `%USERPROFILE%\Pictures\ZSnaper`） |
| `AutoStartOnBoot` | Boolean | `false` | 是否配置注册表实现开机静默自启动 |
| `CaptureHotkey` | String | `"Alt+Q"` | 区域截图全局热键组合 |
| `OcrHotkey` | String | `"Alt+X"` | 截图识字全局热键组合 |

---

## 数据安全与隐私声明

- **本地运算保证**: ZSnaper 在运行期间不建立任何出站 HTTP/HTTPS、WebSocket 或 Socket 网络连接。
- **无用户行为分析**: 程序不包含任何遥测（Telemetry）、指标收集（Analytics）或崩溃日志回传机制。
- **内存安全周期**: 屏幕位图数据在用户完成操作（保存/复制/取消）后立即释放原生资源与托管引用，降低内存驻留风险。

---

## 开源协议与第三方依赖

本项目遵循 [MIT License](LICENSE) 协议开源。

### 主要第三方组件与许可证

- **[SkiaSharp](https://github.com/mono/SkiaSharp)** (v2.88.9) - MIT License (Google Skia Graphics Engine .NET Bindings)
- **[Lucide Icons](https://github.com/lucide-icons/lucide)** - ISC License (Vector UI Icon Assets)
- **[Microsoft.Windows.SDK.NET](https://github.com/microsoft/CsWinRT)** - MIT License (Windows Runtime Projections)

---

Copyright (c) 2026 ZSnaper Contributors.

