# Changelog

## V1.21 Fix21

- CorelDRAW 2026 新版主链改为 `icons.map.xml → Icon GUID → Modern.crlicons`。
- 新增 `IconMapXmlParser` 与 `ModernIconMapBinder`。
- 支持一个 Modern 图标对应多个官方 Icon GUID。
- 只要取得可靠 Icon GUID，即可复制 GUID / `guid://` / `icon=` 并生成 VBA、C++/CPG 模板。
- 顶部“加载Modern.crlicons”调整为“加载新版图标资源”，组合处理 `Modern.crlicons + icons.map.xml`。
- 保持 X8 等旧版 `CrlIcons.dll` 链路不变。

## V1.20 Fix20

- 增加 Modern 图标与 DrawUI 命令的字符串资源关联。
- 增加英文命令名 ↔ Modern 文件名的唯一精确归一化匹配。
- 中文搜索继续复用中文别名词典。

## V1.19 Fix19

- 删除不可稳定使用的“CDR补全预览”和“连接诊断”入口。
- 新增中文智能搜索别名词典。
- 保留英文、快捷键、GUID、资源 ID、文件名和资源路径搜索。

## V1.18 Fix18

- 新增 CorelDRAW 2026 `Modern.crlicons` 原生读取。
- 合并同一逻辑图标的 24 / 48 / 72 PNG 尺寸。

更早版本的详细开发记录可通过 Git 历史继续整理补充。
