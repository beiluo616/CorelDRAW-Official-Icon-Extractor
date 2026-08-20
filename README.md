# CorelDRAW 官方图标提取器

一个面向 CorelDRAW 插件开发与界面研究的 Windows 工具，用于从**用户本机已安装的 CorelDRAW**中浏览、检索、导出官方图标资源，并解析/复制可复用的图标 GUID，生成 VBA 与 C++/CPG `SetIcon2("guid://...")` 示例代码。

> 当前版本：**V1.21 Fix21**  
> 作者：**北落果**  
> 开源协议：**MIT License**

## 主要功能

- 自动检测本机 CorelDRAW 安装，当前重点兼容 X8 ～ 2026/2027 常见目录结构。
- 旧版资源：解析 `CrlIcons.dll`、PE 图标/位图资源及 GUID → 资源 ID 映射。
- 新版资源：解析 `Modern.crlicons + icons.map.xml`，建立官方 **Icon GUID → Modern 图标路径** 映射。
- 解析 DrawUI / Workspace / strings 等 UI 数据，尽可能关联命令名称、快捷键、命令 GUID 与图标 GUID。
- 支持图标墙 / 列表、24/48/72 尺寸预览、筛选与批量勾选。
- 支持中文智能搜索、英文名称、快捷键、GUID、资源 ID、文件名/资源路径搜索。
- 支持复制 `GUID`、`guid://`、`icon="guid://..."`。
- 支持生成 VBA 与 C++/CPG 图标注册模板。
- 支持单个/批量导出 PNG、CSV、JSON 与提取报告。
- 全程只读扫描，不修改 CorelDRAW 安装目录或 UI 配置。

## 2026 新版图标链路

V1.21 已将 CorelDRAW 2026 的主要图标关联切换到真实官方映射：

```text
icons.map.xml
    ↓
Icon GUID
    ↓
Modern.crlicons 资源路径
    ↓
24 / 48 / 72 PNG
```

一个 Modern 图标可能对应多个官方 GUID，程序会显示主 GUID 与其他可用 GUID；只要已确认 Icon GUID，就可以生成 `SetIcon2("guid://...")` 模板，不再强制要求先获得 Command GUID。

## 当前开发状态

项目仍处于 **V1 开发验证阶段**，不是 Alludo/Corel 官方工具。

当前已确认：

- CorelDRAW 2026 的 `icons.map.xml → Modern.crlicons` 主链已可工作，图标 GUID 数量从旧链路的数百提升到数千级。
- 2026 的 VBA / C++/CPG 模板可对已取得 Icon GUID 的 Modern 图标启用。
- 中文搜索基础链路已可工作，例如“二维码”可命中相关图标。

当前仍在继续完善：

- CorelDRAW 2026 中“转曲”等依赖命令名称关联的中文搜索。
- CorelDRAW X8 的中文命令名称/中文搜索解析。
- 少量 Modern 同图不同路径资源的去重细节。

请不要把“扫描到 GUID”直接理解为所有 GUID 都已经逐项完成真实 CorelDRAW 实机验证。

## 开发环境

- Windows 10/11
- C# 12
- .NET 8
- WPF
- Win32 Resource API
- MSTest

项目使用 `global.json` 固定 .NET 8 SDK 系列。

## 编译与发布

在源码根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1
```

发布脚本会执行 Release 测试，并生成 `win-x64` 自包含单文件版本。目标输出：

```text
artifacts\win-x64\CorelDRAW官方图标提取器.exe
```

发布后的 EXE 为绿色版，不要求目标电脑预先安装 .NET 8 Desktop Runtime。

## 基本使用

1. 启动 `CorelDRAW官方图标提取器.exe`。
2. 选择本机 CorelDRAW 版本。
3. 点击 **扫描官方图标**。
4. 通过图标墙、筛选或搜索查找目标图标。
5. 选中图标后，可复制 GUID / `guid://` / `icon=` 引用，或生成 VBA、C++/CPG 模板。

手动研究资源时：

- 旧版可使用 **加载CrlIcons.dll**。
- 新版可使用 **加载新版图标资源**，组合读取 `Modern.crlicons + icons.map.xml`。

## 安全与版权边界

本仓库**不包含、也不应提交** CorelDRAW 官方 DLL、官方图标包或从 CorelDRAW 安装中提取的原始版权资源，例如：

- `CrlIcons.dll`
- `CrlResources.dll`
- `CrlInterop.dll`
- `CrlGenericUI.dll`
- `CorelDRW.exe`
- `Modern.crlicons`
- `icons.map.xml`

程序只读取用户本机安装目录或用户主动选择的本地文件。导出的 CorelDRAW 图标及其他原始资源仍受其权利人的许可范围约束；本项目的 MIT License **只覆盖本项目自身源码**，不授予 CorelDRAW 资源的额外使用权。

CorelDRAW 是其权利人的商标。本项目与 Alludo / Corel 无隶属、授权或官方合作关系。

## 日志与导出

日志默认写入：

```text
%LOCALAPPDATA%\Beiluoguo\CDRIconExtractor\Logs\
```

批量导出可包含 PNG、`icon_index.csv`、`icon_index.json` 与 `extraction_report.txt`。

## 目录结构

```text
src/
  CDRIconExtractor.Core/       核心模型、解析、关联、搜索、导出
  CDRIconExtractor.Windows/    Corel 安装检测、Win32 资源、Automation
  CDRIconExtractor.App/        WPF 主程序

tests/                         MSTest 测试
fixtures/                      仅用于测试的合成/最小化样本
scripts/                       发布脚本与静态审计脚本
docs/                          Windows 实机验收说明
```

## 贡献

欢迎通过 Issue / Pull Request 提交：

- 不同 CorelDRAW 版本的资源结构信息
- X4/X8/2025/2026/2027 兼容性修复
- 图标 GUID 关联改进
- 中文/英文命令名称解析改进
- 搜索别名与测试用例

提交测试样本时，请不要上传 CorelDRAW 官方 DLL、完整图标包或其他无权再分发的原始资源。

## License

本项目自身源码采用 [MIT License](LICENSE)。
