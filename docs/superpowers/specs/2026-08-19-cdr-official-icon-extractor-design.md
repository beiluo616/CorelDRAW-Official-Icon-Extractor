# CDR 官方图标提取器 V1 设计说明

日期：2026-08-19  
产品名：CDR 官方图标提取器  
署名：北落果制作  
交付形态：单文件 Windows EXE，绿色便携，双击运行

## 1. 产品目标

开发一个独立于 F10AI、独立于 CorelDRAW 插件体系的只读辅助工具。工具自动检测本机已安装的 CorelDRAW，读取其 UI 定义与程序资源，建立官方命令/图标索引，并支持搜索、预览和导出。工具自身不携带 CorelDRAW 官方图标，不修改 CorelDRAW 安装目录、工作区、DrawUI.xml、注册表配置或用户配置。

V1 的核心成功标准：用户能够在不启动 CorelDRAW 的情况下，选择一个本机 CorelDRAW 版本，扫描出可识别的官方 UI 命令与可导出的官方图标，搜索“转换为曲线 / Import / Export / Ctrl+Q / GUID”等信息，预览并导出原始尺寸图标到 PNG，同时生成索引文件。

## 2. 已确认产品约束

- 独立 Windows 工具，不集成 F10AI。
- 单文件 EXE，自包含运行时，用户无需安装 .NET。
- 双击即可运行，不需要安装程序。
- 工具界面和“关于”窗口包含“北落果制作”。
- 数据源仅来自用户本机已安装的 CorelDRAW。
- 不内置、不重新分发 CorelDRAW 官方图标库。
- 只读 CorelDRAW 文件；默认不要求管理员权限。
- 不要求 CorelDRAW 正在运行。
- 目标版本优先覆盖 CorelDRAW X8～2027；具体可扫描资源能力按本机版本实际结构适配。

## 3. 参考研究结论

公开项目 Bonus630DevToolsBar 的 DrawUIExplorer 明确支持打开/浏览 CorelDRAW 的 drawui.xml、搜索 UI 元素与 icon extraction；仓库同时包含 DrawUIExplorer、IconCreatorHelper、IconsExtractor.exe、IconLib.dll、Vestris.ResourceLib.dll 等资源相关组件。这证明“解析 UI 定义 + 从本机资源中提取图标”是可行路线，但 V1 不直接复制其二进制文件或源码实现，而是采用独立实现。

CorelDRAW 的 UI 定义中存在 GUID、caption、快捷键、资源索引等信息；不同版本的资源布局可能不同，因此 V1 不把某个固定 DLL 路径或固定资源编号写死为唯一来源，而采用“版本探测器 + UI 索引解析器 + 多资源扫描器 + 关联器”的分层结构。

## 4. 技术方案

### 4.1 技术栈

- C# / .NET 8 Windows
- WPF
- x64 主目标
- `PublishSingleFile=true`
- `SelfContained=true`
- Windows RID：`win-x64`
- 不依赖 WebView2
- 不依赖 CorelDRAW COM Interop 作为 V1 必需项

选择 .NET 8 WPF 的原因：便于生成真正的自包含单文件 EXE；Windows 文件、注册表、PE/资源与 XML 处理能力完整；UI 开发成本低；与 F10AI 代码完全隔离。

### 4.2 模块边界

#### CorelInstallDetector

职责：发现本机 CorelDRAW 安装实例。

输入：Windows 注册表、Program Files 常见目录。  
输出：`CorelInstallation` 列表：版本、显示名、主程序路径、安装根目录、候选 UIConfig 路径。

策略：
1. 优先读取注册表卸载项/产品安装项。
2. 补充扫描常见 Corel 安装目录。
3. 用 `CorelDRW.exe` 存在性验证候选。
4. 不以目录名文本作为唯一版本依据；同时读取文件版本信息。

#### UiDefinitionLocator

职责：在已确认的 CorelDRAW 安装根目录下定位 UI 定义文件。

输出：一个或多个候选 XML，例如 `DrawUI.xml` 或不同版本对应的 UIConfig XML。

规则：只扫描 CorelDRAW 安装根目录及明确的 UIConfig 子目录，不做整盘搜索。

#### DrawUiParser

职责：只读解析 UI XML，提取命令候选。

标准化字段：
- `Guid`
- `GuidRef`
- `Caption`
- `LocalizedCaption`（存在时）
- `Shortcut`（存在时）
- `TagName`
- `ResourceHints`：包括可见的 bmpRow、bmpCol、image、icon、resource 等属性
- `XmlPath`

解析器必须容忍不同版本字段缺失；缺少某个字段不能导致整次扫描失败。

#### PeResourceScanner

职责：扫描 CorelDRAW 安装目录中的候选 PE 文件资源。

V1 支持资源类型：
- RT_ICON
- RT_GROUP_ICON
- RT_BITMAP
- PNG/RCDATA 中能够可靠识别出的 PNG 数据

候选文件优先级：
1. `CorelDRW.exe`
2. UI/Draw 相关 DLL
3. 与 UI 配置同目录或明确资源目录下的 DLL/EXE

不默认递归扫描整个 Corel Graphics Suite 的所有文件；用户可开启“深度扫描”后扩大范围。

#### IconDecoder

职责：把资源转换为统一 `IconAsset`。

字段：
- 来源文件
- 资源类型
- 资源 ID/名称
- 宽、高、位深（可确定时）
- 原始字节哈希
- PNG 预览缓存

多尺寸 ICO/Group Icon 需要保留各尺寸子图，而不是只导出一个放大版本。

#### IconAssociationEngine

职责：将 DrawUI 命令与已提取资源建立关联。

V1 使用分级置信度，而不是假装所有图标都能 100% 自动映射：
- `Exact`：UI 定义中存在可直接解析的资源引用，并唯一命中。
- `Strong`：资源索引组合与资源布局规则唯一命中。
- `Heuristic`：根据邻接/序号/资源组等规则推断。
- `Unmapped`：命令与图标无法可靠关联。

界面必须显示关联置信度；Heuristic 不允许静默冒充 Exact。

#### SearchIndex

支持模糊/包含搜索：
- 中文/本地化 Caption
- 英文 Caption
- GUID
- 快捷键
- 资源 ID
- 来源文件名

#### ExportService

单个导出：保存当前图标全部原始尺寸。  
批量导出：导出当前过滤结果或全部已关联图标。

默认输出目录：EXE 同级 `CDR_Icons_Output\<CorelVersion>\`；如 EXE 所在目录不可写，则回退到用户“文档”目录并提示。

输出结构：

```text
CDR_Icons_Output/
└─ CorelDRAW_2026/
   ├─ Icons/
   │  ├─ <safe-name>_<guid-short>/
   │  │  ├─ 16x16.png
   │  │  ├─ 24x24.png
   │  │  └─ 32x32.png
   ├─ icon_index.csv
   ├─ icon_index.json
   └─ extraction_report.txt
```

文件名使用安全化名称并附短 GUID/资源 ID，避免中文/英文重名覆盖。

## 5. UI 设计

窗口标题：`CDR 官方图标提取器`。

顶部：
- 产品标题
- 小号文字 `北落果制作`
- CorelDRAW 版本下拉框
- “刷新版本”按钮
- 当前安装路径（只读）
- “扫描官方图标”按钮

中部上方：
- 搜索框
- 筛选：全部 / 已关联 / 未关联 / 图标资源
- 视图切换：列表 / 图标墙

列表模式字段：
- 图标预览
- 中文/本地化名称
- 英文/原始 Caption
- 快捷键
- GUID
- 关联状态

图标墙模式：
- 每项显示缩略图与短名称
- 悬停显示完整名称、快捷键、GUID、来源、尺寸、关联置信度

右侧/下方详情区：
- 放大预览（仅做最近邻/高质量 UI 预览，不改变导出原图）
- 命令信息
- 资源信息
- 可用原始尺寸
- `导出当前`
- `打开输出目录`

底部状态栏：
- 扫描文件数
- 命令数
- 图标资源数
- 已关联数
- 未关联数
- 当前状态
- `北落果制作`

关于窗口：

```text
CDR 官方图标提取器
Version 1.0
制作：北落果

本工具仅从用户本机已安装的 CorelDRAW
程序资源中读取并导出图标。
```

## 6. 扫描数据流

```text
启动 EXE
  ↓
探测 CorelDRAW 安装实例
  ↓
用户选择版本
  ↓
定位 UI 定义 + 候选 PE 资源
  ↓
并行：解析 DrawUI + 扫描资源
  ↓
解码图标资产
  ↓
建立命令索引
  ↓
关联命令 ↔ 图标资源
  ↓
显示列表/图标墙
  ↓
搜索 / 预览 / 导出
```

扫描任务运行在后台，UI 可取消。取消后保留已完成结果并标记“扫描未完成”。

## 7. 错误处理

- 未检测到 CDR：显示“未检测到已安装的 CorelDRAW”，允许用户手动选择 `CorelDRW.exe`。
- 找不到 DrawUI：仍可进入“纯资源扫描模式”，但命令名称关联能力受限，并明确提示。
- 某个 DLL 读取失败：记录日志，继续扫描其他资源；不因单文件失败终止整次任务。
- 资源格式不支持：标记 Unsupported，保留来源信息，不崩溃。
- XML 解析异常：报告具体文件；不修改原文件。
- 输出目录不可写：自动切换到用户文档目录并显示实际路径。
- 同名文件：永不覆盖，使用 GUID/资源 ID/数字后缀去重。

## 8. 日志与隐私

日志默认写入：`%LOCALAPPDATA%\Beiluoguo\CDRIconExtractor\Logs\`。

日志记录：
- 工具版本
- Windows 版本
- CorelDRAW 文件版本
- 被扫描文件路径
- 解析/导出错误
- 数量与耗时

不采集、不上传任何文件或遥测。V1 完全离线。

## 9. 性能目标

在 SSD、本机单个 CorelDRAW 版本上：
- 普通扫描目标：10 秒内给出首批可浏览结果。
- 完整普通扫描目标：30 秒内完成；具体取决于版本和资源数量。
- UI 在扫描期间保持响应。
- 图片预览使用懒加载与缩略图缓存，避免一次性解码全部大资源。

性能目标不是硬性承诺；V1 日志必须拆分记录“安装探测 / XML 解析 / PE 扫描 / 解码 / 关联 / UI 建索引”耗时，便于实机优化。

## 10. 测试策略

### 单元测试

- Corel 安装版本解析
- XML 属性容错
- GUID/快捷键/Caption 索引
- PE 资源枚举
- Group Icon 多尺寸解析
- PNG 签名识别
- 文件名安全化与去重
- CSV/JSON 索引输出
- 关联置信度规则

### 夹具测试

由于 CI 环境不应捆绑 CorelDRAW 官方二进制，测试仓库使用自制 PE/XML fixture：
- 含 ICON/GROUP_ICON/BITMAP/PNG 资源的小型测试 EXE/DLL
- 模拟不同版本字段差异的 DrawUI XML
- 损坏/缺失资源 fixture

### Windows 实机验收

至少验证：
- 一个较老版本（优先 X8 或 2019）
- 一个中间版本
- CorelDRAW 2026

验收重点：
- 能正确检测版本
- 不启动/不修改 CorelDRAW
- 转换为曲线、导入、导出三个目标命令可以被搜索到（若该版本 UI 定义包含相应条目）
- 对应图标若能可靠关联，导出图像与 CDR UI 视觉一致
- 若不能自动关联，工具明确显示 Unmapped/Heuristic，不给错误的“官方对应”结论

## 11. V1 明确不做

- 不修改或注入 CorelDRAW UI。
- 不写入 DrawUI.xml / workspace。
- 不通过截图抓取菜单图标。
- 不做 SVG 自动矢量化。
- 不做图标编辑器。
- 不内置 Corel 官方图标资源。
- 不上传网络。
- 不依赖用户启动 CorelDRAW。
- 不承诺所有历史版本的每一个命令都能自动 100% 映射图标；无法可靠映射时必须明确展示。

## 12. 交付物

V1 交付：
- `CDR官方图标提取器.exe`：win-x64 自包含单文件。
- `CDR官方图标提取器_V1_FullSource.zip`：完整源码、测试、发布脚本与 README。
- 源码内设计文档与实现计划。

最终 EXE 外部不要求携带 DLL、配置文件或运行时目录；运行时日志和导出目录按需创建。
