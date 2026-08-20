# CorelDRAW官方图标提取器 V1.21 Fix21 — Windows 实机验收

## 发布
- [ ] 在源码根目录执行：`powershell -ExecutionPolicy Bypass -File .\scripts\publish-win-x64.ps1`。
- [ ] Release 测试全部通过。
- [ ] `artifacts\win-x64` 最终只有 `CorelDRAW官方图标提取器.exe`。
- [ ] 双击 EXE 正常启动。
- [ ] 关于窗口显示 `Version 1.21`。

## X8 回归
- [ ] 选择 CorelDRAW X8 后“扫描官方图标”正常。
- [ ] X8 的 CrlIcons.dll 图标/GUID 数量没有明显回退。
- [ ] 选中已知 X8 图标后可复制 GUID，并可生成 VBA / C++/CPG 模板。

## 2026 icons.map.xml 主链
- [ ] 选择 CorelDRAW 2026，点击“扫描官方图标”。
- [ ] 程序自动找到 `Modern.crlicons` 和 `icons.map.xml`；无需手工选文件。
- [ ] 底部 `Modern有GUID` 应接近绝大多数 Modern 资源，不再只有个位数。
- [ ] 底部 `映射GUID` 应为数千级，而不是旧链路约 568。
- [ ] `图标GUID` 总量明显高于 V1.20。
- [ ] 选择 `TB_Ungroup` 等有官方映射的图标，右侧显示图标 GUID 和 `GUID映射来源`。
- [ ] 一个图片存在多个 GUID 时，右侧“其他可用图标 GUID”直接展开显示。
- [ ] 只要有 Icon GUID，即使命令 GUID 尚未关联，`生成 VBA 模板 / 生成 C++/CPG 模板` 仍可点击。
- [ ] 生成模板继续使用 `SetIcon2("guid://...")`，并包含图标资源路径/GUID 来源注释。

## 手动加载新版资源
- [ ] 顶部按钮显示“加载新版图标资源”。
- [ ] 选择 `Modern.crlicons` 后，若同目录/安装目录存在 `icons.map.xml`，程序自动加载。
- [ ] 自动找不到时会提示选择 `icons.map.xml`，取消时不把不完整结果伪装为已完整加载。
- [ ] 加载完成状态显示图标套数、有 GUID 资源数、官方映射 GUID 数。

## 中文搜索
- [ ] 2026 完整扫描后搜索 `转曲` 能命中 Convert to Curves 对应命令/图标（前提是本机 DrawUI/名称数据确有该命令）。
- [ ] 搜索 `焊接 / 解组 / 二维码` 能按别名匹配相应英文命令。
- [ ] 英文、快捷键、GUID、资源路径搜索继续正常。
