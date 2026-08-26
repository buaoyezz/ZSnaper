<p align="center">
  <img src="assets/banner.png" alt="ZSnaper — Windows screenshot and offline OCR" width="100%" />
</p>

<p align="center">
  <strong>捕捉、标注、识别——全部在本地完成。</strong>
</p>

<p align="center">
  <a href="#核心体验">核心体验</a> ·
  <a href="#快捷键">快捷键</a> ·
  <a href="#构建">构建</a> ·
  <a href="#配置">配置</a> ·
  <a href="#许可证">许可证</a>
</p>

ZSnaper 是一款面向 Windows 的轻量截图与离线 OCR 工具。它将智能选区、图像标注和文字识别收进一条紧凑流程，不上传截图，也不依赖在线识别服务。

## 核心体验

<table>
  <tr>
    <td width="33%" valign="top">
      <strong>智能捕捉</strong><br />
      自动识别窗口、页面与控件；也可随时拖出自由选区，并继续移动或缩放。
    </td>
    <td width="33%" valign="top">
      <strong>轻量标注</strong><br />
      画笔、箭头、文字与马赛克集中在悬浮工具栏中，支持撤销和画布重置。
    </td>
    <td width="33%" valign="top">
      <strong>离线 OCR</strong><br />
      基于 Windows.Media.Ocr 在本机识别文字，并提供图像预处理与段落清理。
    </td>
  </tr>
</table>

- 支持多显示器虚拟桌面捕获与鼠标指针合成
- 支持复制、保存、自动保存及截图后直接 OCR
- 支持全局快捷键、系统托盘和开机启动
- 支持浅色、深色主题及自定义强调色

## 快捷键

| 快捷键 | 操作 |
| :--- | :--- |
| `Alt + Q` | 启动截图 |
| `Alt + X` | 启动截图并识别文字 |
| `Enter` | 完成当前截图 |
| `Ctrl + C` | 复制截图或识别文本 |
| `Ctrl + S` | 保存截图 |
| `Ctrl + Z` | 撤销上一步标注 |
| `R` | 重置选区 |
| `~` | 切换是否包含鼠标指针 |
| `Esc` | 取消截图或关闭结果窗口 |

## 构建

### 环境

- Windows 10 1809（Build 17763）或更高版本
- .NET 8 SDK
- 对应语言的 Windows OCR 语言包

### 编译

```powershell
dotnet restore
dotnet build ZSnaper.csproj -c Release
```

### Windows x64 单文件发布

```powershell
dotnet publish ZSnaper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o ./publish
```

## 配置

用户配置保存在：

```text
%APPDATA%\ZSnaper\config.json
```

可在应用内调整主题、动画、强调色、快捷键、自动复制/保存、工具栏位置、标注样式和 OCR 段落清理策略。

## 隐私

截图、图像预处理与 OCR 识别均在本地执行。ZSnaper 不需要将图片上传到远程服务器，也不包含数据上报流程。

## 许可证

ZSnaper 以 [GNU General Public License v3.0](LICENSE) 发布。你可以在 GPLv3 条款下使用、研究、修改和重新分发本项目。
