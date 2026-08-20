using System.Reflection;
using System.Runtime.InteropServices;
using CDRIconExtractor.Core.Parsing;

namespace CDRIconExtractor.Windows.Automation;

public sealed class CorelRunningLocalizationProvider : ILocalizedStringProvider, IUiCaptionProvider, IDisposable
{
    private readonly CorelRunningConnection _connection;
    private object? _frameWork;
    private object? _automation;

    private CorelRunningLocalizationProvider(CorelRunningConnection connection) => _connection = connection;

    public CorelConnectionDiagnostic ConnectionDiagnostic => _connection.Diagnostic;

    public static CorelRunningLocalizationProvider? TryConnect(int versionMajor) => TryConnect(versionMajor, out _);

    public static CorelRunningLocalizationProvider? TryConnect(int versionMajor, out CorelConnectionDiagnostic diagnostic)
    {
        var connection = CorelRunningInstanceConnector.TryConnect(versionMajor, out diagnostic);
        return connection is null ? null : new CorelRunningLocalizationProvider(connection);
    }

    public string? LoadLocalizedString(string guid)
    {
        try
        {
            var application = _connection.Application;
            var value = application.GetType().InvokeMember(
                "LoadLocalizedString",
                BindingFlags.InvokeMethod,
                binder: null,
                target: application,
                args: new object?[] { guid });
            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public string? GetCaptionText(string guid)
    {
        try
        {
            var automation = EnsureAutomation();
            if (automation is null)
                return null;
            var value = automation.GetType().InvokeMember(
                "GetCaptionText",
                BindingFlags.InvokeMethod,
                binder: null,
                target: automation,
                args: new object?[] { guid });
            return value?.ToString();
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        ReleaseCom(Interlocked.Exchange(ref _automation, null));
        ReleaseCom(Interlocked.Exchange(ref _frameWork, null));
        _connection.Dispose();
    }

    private object? EnsureAutomation()
    {
        if (_automation is not null)
            return _automation;
        var application = _connection.Application;
        _frameWork ??= application.GetType().InvokeMember(
            "FrameWork",
            BindingFlags.GetProperty,
            binder: null,
            target: application,
            args: null);
        if (_frameWork is null)
            return null;
        _automation = _frameWork.GetType().InvokeMember(
            "Automation",
            BindingFlags.GetProperty,
            binder: null,
            target: _frameWork,
            args: null);
        return _automation;
    }

    private static void ReleaseCom(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
            return;
        try { Marshal.FinalReleaseComObject(value); }
        catch { }
    }
}
