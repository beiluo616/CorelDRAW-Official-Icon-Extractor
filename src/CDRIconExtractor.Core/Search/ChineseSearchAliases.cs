namespace CDRIconExtractor.Core.Search;

/// <summary>
/// Designer-facing Chinese aliases for common CorelDRAW commands.
/// The dictionary intentionally maps everyday Chinese terms to the English names
/// normally exposed by DrawUI/Workspace data, so search still works when a local
/// installation does not expose a Chinese caption for a command.
/// </summary>
internal static class ChineseSearchAliases
{
    private static readonly IReadOnlyDictionary<string, string[]> AliasMap = Build();

    public static IEnumerable<string> Expand(string normalizedQuery)
    {
        if (string.IsNullOrWhiteSpace(normalizedQuery))
            yield break;

        if (!AliasMap.TryGetValue(normalizedQuery, out var aliases))
            yield break;

        foreach (var alias in aliases)
            yield return alias;
    }

    private static IReadOnlyDictionary<string, string[]> Build()
    {
        var map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        AddGroup(map, ["转曲", "转为曲线", "转换为曲线", "转换曲线", "Convert to Curves", "Ctrl+Q"]);
        AddGroup(map, ["轮廓转对象", "轮廓转换对象", "轮廓转曲", "Convert Outline to Object", "Ctrl+Shift+Q"]);
        AddGroup(map, ["群组", "编组", "Group", "Ctrl+G"]);
        AddGroup(map, ["解组", "取消群组", "解散群组", "Ungroup", "Ctrl+U"]);
        AddGroup(map, ["合并", "Combine", "Ctrl+L"]);
        AddGroup(map, ["打散", "拆分对象", "分离对象", "Break Apart", "Ctrl+K"]);

        AddGroup(map, ["焊接", "Weld"]);
        AddGroup(map, ["修剪", "Trim"]);
        AddGroup(map, ["相交", "Intersect", "Intersection"]);
        AddGroup(map, ["简化", "Simplify"]);
        AddGroup(map, ["前减后", "Front minus Back", "Front Minus Back"]);
        AddGroup(map, ["后减前", "Back minus Front", "Back Minus Front"]);

        AddGroup(map, ["左对齐", "靠左对齐", "Align Left"]);
        AddGroup(map, ["右对齐", "靠右对齐", "Align Right"]);
        AddGroup(map, ["顶部对齐", "上对齐", "Align Top"]);
        AddGroup(map, ["底部对齐", "下对齐", "Align Bottom"]);
        AddGroup(map, ["水平居中", "水平中心", "Center Horizontally", "Horizontal Center"]);
        AddGroup(map, ["垂直居中", "垂直中心", "Center Vertically", "Vertical Center"]);
        AddGroup(map, ["页面居中", "居中页面", "Center to Page"]);

        AddGroup(map, ["轮廓图", "等高线", "Contour"]);
        AddGroup(map, ["调和", "渐变调和", "Blend"]);
        AddGroup(map, ["阴影", "投影", "Drop Shadow", "Shadow"]);
        AddGroup(map, ["透明度", "透明", "Transparency"]);
        AddGroup(map, ["封套", "Envelope"]);
        AddGroup(map, ["变形", "Distort"]);
        AddGroup(map, ["立体化", "立体", "Extrude"]);

        AddGroup(map, ["导入", "Import", "Ctrl+I"]);
        AddGroup(map, ["导出", "Export", "Ctrl+E"]);
        AddGroup(map, ["二维码", "QR码", "QR Code", "QRCode", "QRcode"]);
        AddGroup(map, ["条形码", "条码", "Barcode"]);

        AddGroup(map, ["选择工具", "挑选工具", "Pick Tool", "Select Tool"]);
        AddGroup(map, ["形状工具", "节点工具", "Shape Tool"]);
        AddGroup(map, ["贝塞尔", "贝塞尔工具", "Bezier", "Bezier Tool"]);
        AddGroup(map, ["手绘", "手绘工具", "Freehand", "Freehand Tool"]);
        AddGroup(map, ["矩形工具", "矩形", "Rectangle Tool", "Rectangle"]);
        AddGroup(map, ["椭圆工具", "椭圆", "Ellipse Tool", "Ellipse"]);
        AddGroup(map, ["文本工具", "文字工具", "Text Tool"]);
        AddGroup(map, ["裁剪工具", "裁剪", "Crop Tool", "Crop"]);
        AddGroup(map, ["缩放工具", "缩放", "Zoom Tool", "Zoom"]);
        AddGroup(map, ["平移工具", "平移", "Pan Tool", "Pan"]);
        AddGroup(map, ["填充", "Fill"]);
        AddGroup(map, ["轮廓", "Outline"]);

        return map;
    }

    private static void AddGroup(Dictionary<string, string[]> map, string[] terms)
    {
        var aliases = terms.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        foreach (var term in aliases.Where(ContainsCjk))
            map[NormalizeKey(term)] = aliases.Where(x => !string.Equals(x, term, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private static bool ContainsCjk(string value) => value.Any(ch =>
        ch is >= '\u3400' and <= '\u9FFF' || ch is >= '\uF900' and <= '\uFAFF');

    private static string NormalizeKey(string value)
    {
        var chars = value.Where(ch => char.IsLetterOrDigit(ch) || ch > 127).Select(char.ToUpperInvariant);
        return new string(chars.ToArray());
    }
}
