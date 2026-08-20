using System.Text;

namespace CDRIconExtractor.Core.Utilities;

public sealed record IconRegistrationTemplateItem(string IconGuid, string CommandId, string Caption);

public static class IconRegistrationTemplateGenerator
{
    public static string GenerateVba(
        string iconGuid,
        string macroCommand = "MyMacro.MyModule.MyCommand",
        string caption = "我的功能",
        string? resourcePath = null,
        string? guidSource = null)
    {
        var guid = RequireGuid(iconGuid);
        macroCommand = EscapeVba(macroCommand);
        caption = EscapeVba(caption);
        var uri = $"guid://{guid}";

        var provenance = BuildVbaProvenance(resourcePath, guidSource);
        return $"' CorelDRAW 内部图标注册模板 - By北落果\r\n" +
               $"' 图标 GUID: {guid}\r\n" +
               provenance +
               "Dim ctl As CommandBarControl\r\n\r\n" +
               "Set ctl = Application.CommandBars(\"Standard\").Controls.AddCustomButton( _\r\n" +
               "    cdrCmdCategoryMacros, _\r\n" +
               $"    \"{macroCommand}\", _\r\n" +
               "    Temporary:=True)\r\n\r\n" +
               $"ctl.Caption = \"{caption}\"\r\n" +
               $"ctl.ToolTipText = \"{caption}\"\r\n" +
               $"ctl.SetIcon2 \"{uri}\"\r\n";
    }

    public static string GenerateCpp(
        string iconGuid,
        string commandId = "MyCommand",
        string caption = "我的功能",
        string? resourcePath = null,
        string? guidSource = null)
    {
        var guid = RequireGuid(iconGuid);
        commandId = EscapeCpp(commandId);
        caption = EscapeCpp(caption);
        var uri = $"guid://{guid}";

        var sb = new StringBuilder();
        sb.AppendLine("// CorelDRAW C++/CPG 内部图标注册模板 - By北落果");
        sb.AppendLine($"// 图标 GUID: {guid}");
        if (!string.IsNullOrWhiteSpace(resourcePath))
            sb.AppendLine($"// 图标资源: {resourcePath.Trim()}");
        if (!string.IsNullOrWhiteSpace(guidSource))
            sb.AppendLine($"// GUID 来源: {guidSource.Trim()}");
        sb.AppendLine($"m_pApp->AddPluginCommand(_bstr_t(\"{commandId}\"), _bstr_t(\"{caption}\"), _bstr_t(\"{caption}\"));");
        sb.AppendLine();
        sb.AppendLine("VGCore::ICUIControlPtr ctl = m_pApp->CommandBars");
        sb.AppendLine("    ->Item[_bstr_t(\"Standard\")]");
        sb.AppendLine("    ->Controls");
        sb.AppendLine("    ->AddCustomButton(VGCore::cdrCmdCategoryPlugins, _bstr_t(\"" + commandId + "\"), 1, VARIANT_FALSE);");
        sb.AppendLine();
        sb.AppendLine($"ctl->SetIcon2(_bstr_t(\"{uri}\"));");
        return sb.ToString();
    }

    public static string GenerateVbaBatch(IEnumerable<IconRegistrationTemplateItem> source, string commandBarName = "Standard")
    {
        var items = NormalizeItems(source).ToArray();
        if (items.Length == 0)
            throw new ArgumentException("At least one valid icon GUID is required.", nameof(source));

        var barName = EscapeVba(commandBarName);
        var sb = new StringBuilder();
        sb.AppendLine("' CorelDRAW 批量内部图标注册模板 - By北落果");
        sb.AppendLine($"' 按钮数量: {items.Length}");
        sb.AppendLine("Dim bar As CommandBar");
        for (var i = 0; i < items.Length; i++)
            sb.AppendLine($"Dim ctl{i + 1} As CommandBarControl");
        sb.AppendLine();
        sb.AppendLine($"Set bar = Application.CommandBars(\"{barName}\")");
        sb.AppendLine();

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var n = i + 1;
            var command = EscapeVba(item.CommandId);
            var caption = EscapeVba(item.Caption);
            sb.AppendLine($"' [{n}] {caption}");
            sb.AppendLine($"Set ctl{n} = bar.Controls.AddCustomButton( _");
            sb.AppendLine("    cdrCmdCategoryMacros, _");
            sb.AppendLine($"    \"{command}\", _");
            sb.AppendLine("    Temporary:=True)");
            sb.AppendLine($"ctl{n}.Caption = \"{caption}\"");
            sb.AppendLine($"ctl{n}.ToolTipText = \"{caption}\"");
            sb.AppendLine($"ctl{n}.SetIcon2 \"guid://{item.IconGuid}\"");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string GenerateCppBatch(IEnumerable<IconRegistrationTemplateItem> source, string commandBarName = "Standard")
    {
        var items = NormalizeItems(source).ToArray();
        if (items.Length == 0)
            throw new ArgumentException("At least one valid icon GUID is required.", nameof(source));

        var barName = EscapeCpp(commandBarName);
        var sb = new StringBuilder();
        sb.AppendLine("// CorelDRAW C++/CPG 批量内部图标注册模板 - By北落果");
        sb.AppendLine($"// 按钮数量: {items.Length}");
        sb.AppendLine();

        for (var i = 0; i < items.Length; i++)
        {
            var item = items[i];
            var n = i + 1;
            var command = EscapeCpp(item.CommandId);
            var caption = EscapeCpp(item.Caption);
            sb.AppendLine($"// [{n}] {caption}");
            sb.AppendLine($"m_pApp->AddPluginCommand(_bstr_t(\"{command}\"), _bstr_t(\"{caption}\"), _bstr_t(\"{caption}\"));");
            sb.AppendLine($"VGCore::ICUIControlPtr ctl{n} = m_pApp->CommandBars");
            sb.AppendLine($"    ->Item[_bstr_t(\"{barName}\")]");
            sb.AppendLine("    ->Controls");
            sb.AppendLine($"    ->AddCustomButton(VGCore::cdrCmdCategoryPlugins, _bstr_t(\"{command}\"), {n}, VARIANT_FALSE);");
            sb.AppendLine($"ctl{n}->SetIcon2(_bstr_t(\"guid://{item.IconGuid}\"));");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string BuildVbaProvenance(string? resourcePath, string? guidSource)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(resourcePath))
            sb.Append("' 图标资源: ").Append(resourcePath.Trim()).Append("\r\n");
        if (!string.IsNullOrWhiteSpace(guidSource))
            sb.Append("' GUID 来源: ").Append(guidSource.Trim()).Append("\r\n");
        return sb.ToString();
    }

    private static IEnumerable<IconRegistrationTemplateItem> NormalizeItems(IEnumerable<IconRegistrationTemplateItem> source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var index = 0;
        foreach (var raw in source)
        {
            index++;
            var guid = IconGuidReference.Normalize(raw.IconGuid);
            if (guid is null)
                continue;
            var command = string.IsNullOrWhiteSpace(raw.CommandId) ? $"MyCommand{index}" : raw.CommandId.Trim();
            var caption = string.IsNullOrWhiteSpace(raw.Caption) ? $"我的功能{index}" : raw.Caption.Trim();
            yield return new IconRegistrationTemplateItem(guid, command, caption);
        }
    }

    private static string RequireGuid(string value) =>
        IconGuidReference.Normalize(value) ?? throw new ArgumentException("A valid icon GUID is required.", nameof(value));

    private static string EscapeVba(string value) => (value ?? string.Empty).Replace("\"", "\"\"");
    private static string EscapeCpp(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
}
