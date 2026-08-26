
<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo/icon-dark.svg">
    <img src="assets/logo/icon-light.svg" alt="ZSnaper Logo" width="25%" />
  </picture>
</p>

<p align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset="assets/logo/text-dark.svg">
    <img src="assets/logo/text-light.svg" alt="ZSnaper" width="320" />
  </picture>
</p>
<p align="center">
  <strong>Zip、Snip、Faster</strong>
</p>

<p align="center">
  <a href="#快捷键">快捷键</a> ·
  <a href="#构建">构建</a> ·
  <a href="#配置">配置</a> ·
  <a href="#许可证">许可证</a>
</p>

<p align="center">
  <a href="LICENSE"><img src="https://img.shields.io/badge/License-GPLv3-blue.svg" alt="License: GPL v3" /></a>
  <img src="https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet&logoColor=white" alt=".NET 8.0" />
  <img src="https://img.shields.io/badge/C%23-12-239120?logo=csharp&logoColor=white" alt="C# 12" />
  <img src="https://img.shields.io/badge/Platform-Windows%2010%2B-0078D6?logo=windows&logoColor=white" alt="Windows 10+" />
  <a href="#隐私"><img src="https://img.shields.io/badge/OCR-100%25%20Offline-success?logo=shield" alt="100% Offline" /></a>
  <a href="https://github.com/ZZBuAoYe/ZSnaper/releases"><img src="https://img.shields.io/github/v/release/ZZBuAoYe/ZSnaper?color=orange&label=Version" alt="Latest Release" /></a>
</p>

`ZSnaper` 是一款面向 Windows 的轻量截图工具并基于`Windows.Media.Ocr`提供本地离线的快速 OCR 能力,我们将智能选区、图像标注和文字识别放在了一个功能栏，
> 本软件不上传截图，也不依赖在线识别服务(但后续`可能会支持`接入自己的OCR大模型)

<p align="center">
  <img src="assets/banner.png" alt="ZSnaper — Windows screenshot and offline OCR" width="100%" />
</p>

## 默认快捷键

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

## 构建项目

### 所需环境

- Windows 10 1809（Build 17763）或更高版本
- .NET 8 SDK
- 对应语言的 Windows OCR 语言包

### 常规编译

```powershell
dotnet restore
dotnet build ZSnaper.csproj -c Release
```

### Windows x64 下的单文件发布

```powershell
dotnet publish ZSnaper.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -o ./publish
```

## 相关配置

用户配置默认保存在：

```bash
%APPDATA%\ZSnaper\config.json
```
您可以通过:
```cmd
explorer %APPDATA%\ZSnaper\
```
快速打开此目录

APP支持在应用内调整`主题`、`动画`、`强调色`、`快捷键`、`自动复制/保存`、`工具栏位置`、`标注样式`和 `OCR 段落清理`策略

## 隐私相关

本项目的`全部`截图、图像预处理与 OCR 识别`均在本地执行`<br>
ZSnaper 不需要也不会将图片上传到远程服务器，软件内也不包含数据上报流程<br>
本项目遵循 `GPL-3.0` 协议开源，所有版本更新与相关说明均以中文版本为准并优先维护!

## 许可证

ZSnaper 基于 [GNU General Public License v3.0](LICENSE) 开源协议发布
你可以在该条款下自由使用、研究、修改和重新分发本项目

Copyright © 2026 [ZZBuAoYe](https://github.com/buaoyezz). All rights reserved.
