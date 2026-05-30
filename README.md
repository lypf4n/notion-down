# NotionDown

[![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com)
[![License](https://img.shields.io/badge/license-MIT-green)](LICENSE)

将 Notion 页面递归导出为 Markdown 文件，目录结构对齐 Notion 原生导出，支持图片下载、过期链接修复。

## 功能

- **递归导出**：根页面及其所有子页面完整导出
- **目录对齐**：仿 Notion "为子页面创建文件夹"，`.md` 与图片文件夹并列
- **图片下载**：自动下载 Notion 托管图片，命名 `image_1.png`、`image_2.png`……
- **子页面链接**：自动转为正确的相对路径（空格编码为 `%20`，兼容 Obsidian/思源）
- **API 重试**：429/502/503/504 自动退避重试
- **修复模式**：扫描 `.md` 残留的远程链接，按位置编号重新下载
- **特殊字符修复**：`C#` → `CSharp`，代码块 ````c#` → ````csharp`

## 环境

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- [Notion Integration](https://www.notion.so/my-integrations)（获取 API Token）

## 快速开始

```bash
# 1. 克隆项目
git clone <repo-url> && cd NotionDown

# 2. 配置 Token
#    编辑 NotionDown.Console/appsettings.Development.json，填入你的 Notion API Token
#    （该文件已在 .gitignore 中，不会提交到仓库）

# 3. 导出页面
dotnet run --project NotionDown.Console -- <page-url> [output-dir]
```

> **注意**：页面需要在 Notion 中授权给集成（右上角 `···` → 连接 → 选择你的集成）。

## 用法

```bash
# 导出（使用配置文件中的默认输出目录）
dotnet run --project NotionDown.Console -- https://www.notion.so/MyPage-abc123

# 导出到指定目录
dotnet run --project NotionDown.Console -- https://www.notion.so/MyPage-abc123 "D:\notes"

# 修复过期/下载失败的图片
dotnet run --project NotionDown.Console -- --repair "D:\notes"

# 特殊字符修复（# → Sharp）
dotnet run --project NotionDown.Console -- --fix "D:\notes"
```

## 配置

`appsettings.json` 提供默认值（可提交），`appsettings.Development.json` 存放真实密钥（已 gitignore）：

```json
{
  "Notion": {
    "Token": "secret_xxx"
  },
  "Export": {
    "OutputDir": "./notion-output"
  }
}
```

## 导出目录结构

```
notion-output/
├── 我的笔记/                  ← 根页面作为包装文件夹（重名自动加 (1)）
│   ├── 我的笔记.md            ← 根页面内容
│   ├── 我的笔记/              ← 根页面的图片 + 子页面
│   │   ├── image_1.png
│   │   ├── 第一章 入门.md      ← 子页面（平铺，不在文件夹里）
│   │   ├── 第一章 入门/        ← 子页面的图片（与 .md 并列）
│   │   │   ├── image_1.png
│   │   │   └── image_2.png
│   │   └── ...
```



