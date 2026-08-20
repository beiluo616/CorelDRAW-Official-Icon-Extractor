using System.Runtime.InteropServices;
using CDRIconExtractor.Core.Utilities;

namespace CDRIconExtractor.Windows.Automation;

public sealed record IconGuidValidationResult(bool Accepted, string Message);

public sealed class CorelRunningIconValidator : IDisposable
{
    private const string PluginCategoryGuid = "ab489730-8791-45d2-a825-b78bbe0d6a5d";
    private const int CuiBarFloating = 4;
    private readonly CorelRunningConnection _connection;

    private CorelRunningIconValidator(CorelRunningConnection connection) => _connection = connection;

    public CorelConnectionDiagnostic ConnectionDiagnostic => _connection.Diagnostic;

    public static CorelRunningIconValidator? TryConnect(int versionMajor) => TryConnect(versionMajor, out _);

    public static CorelRunningIconValidator? TryConnect(int versionMajor, out CorelConnectionDiagnostic diagnostic)
    {
        var connection = CorelRunningInstanceConnector.TryConnect(versionMajor, out diagnostic);
        return connection is null ? null : new CorelRunningIconValidator(connection);
    }

    public IconGuidValidationResult Validate(string iconGuid)
    {
        var normalized = IconGuidReference.Normalize(iconGuid);
        if (normalized is null)
            return new IconGuidValidationResult(false, "图标 GUID 格式无效。");

        object? barObject = null;
        object? controlObject = null;
        var application = _connection.Application;
        var probeId = $"CDRIconExtractor.IconProbe.{Guid.NewGuid():N}";
        try
        {
            dynamic app = application;
            _ = app.AddPluginCommand(probeId, "CDR 图标 GUID 验证", "CDR 图标 GUID 验证");

            dynamic frameWork = app.FrameWork;
            dynamic bars = frameWork.CommandBars;
            dynamic bar = bars.Add($"CDRIconProbe_{Guid.NewGuid():N}", CuiBarFloating, true);
            barObject = bar;
            bar.Visible = false;

            dynamic control = bar.Controls.AddCustomButton(PluginCategoryGuid, probeId, 0, true);
            controlObject = control;
            control.Visible = false;
            control.SetIcon2($"guid://{normalized}");

            return new IconGuidValidationResult(
                true,
                $"CorelDRAW 已接受 guid://{normalized} 的 SetIcon2 调用；连接方式：{_connection.Method}。此结果验证 GUID 可被 UI API 使用，但不代表本工具已取得预览图片。");
        }
        catch (Exception ex)
        {
            return new IconGuidValidationResult(false, $"CorelDRAW 拒绝或无法验证该图标 GUID：{Unwrap(ex).Message}");
        }
        finally
        {
            if (barObject is not null)
            {
                try { ((dynamic)barObject).Delete(); }
                catch { }
            }
            try { ((dynamic)application).RemovePluginCommand(probeId); }
            catch { }
            ReleaseCom(controlObject);
            ReleaseCom(barObject);
        }
    }

    public void Dispose() => _connection.Dispose();

    private static Exception Unwrap(Exception ex)
    {
        while (ex.InnerException is not null)
            ex = ex.InnerException;
        return ex;
    }

    private static void ReleaseCom(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
            return;
        try { Marshal.FinalReleaseComObject(value); }
        catch { }
    }
}
