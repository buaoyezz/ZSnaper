<p align="center">
  <img src="assets/banner.png" alt="ZSnaper Banner" width="100%" />
</p>

# ZSnaper

ZSnaper 是一个基于 .NET 8 开发的 Windows 桌面截图与本地离线 OCR 工具，支持屏幕区域捕获、图像标注与本地文字识别。

## 功能

- 屏幕截图：支持虚拟屏幕多显示器捕获、窗口与控件边界探测、鼠标指针捕获。
- 图像标注：提供画笔、箭头、文字、马赛克等工具，支持多级撤销与画布重置。
- 离线 OCR：基于 Windows.Media.Ocr 原生接口实现本地文本识别，支持图像预处理与段落清洗。
- 快捷键与托盘：支持全局快捷键唤起，支持系统托盘后台运行与开机启动配置。

## 环境要求

- Windows 10 (Version 1809 / Build 17763.0 或更高版本) / Windows 11
- .NET Desktop Runtime 8.0（使用独立单文件发布包时无需安装）
- Windows OCR 语言包（系统自带，或在「设置 > 时间和语言 > 语言」中安装目标语言）

## 构建与发布

### 编译

```powershell
dotnet restore
dotnet build -c Release
```

### 单文件发布

```powershell
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ./publish
```

## 快捷键

| 快捷键 | 场景 | 说明 |
| :--- | :--- | :--- |
| `Alt + Q` | 全局 | 启动截图 |
| `Alt + X` | 全局 | 启动截图并识别文本 |
| `Enter` | 截图界面 | 完成截图 |
| `Ctrl + C` | 截图 / OCR 界面 | 复制图像 / 复制识别文本 |
| `Ctrl + S` | 截图界面 | 保存图像到本地 |
| `Ctrl + Z` | 截图界面 | 撤销上一步标注 |
| `Esc` | 截图 / OCR 界面 | 退出截图 / 关闭结果窗口 |
| `~` | 截图界面 | 切换是否包含鼠标光标 |

## 配置

配置文件路径：`%APPDATA%\ZSnaper\config.json`

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

## 隐私说明

所有图像截取与 OCR 识别均在本地执行，不进行任何网络通信与数据上报。

## 许可证

[MIT](LICENSE)
