using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;

namespace CDRIconExtractor.Windows.Automation;

public sealed record CorelProcessInfo(
    int ProcessId,
    int? VersionMajor,
    string? FileVersion,
    string? ExecutablePath,
    string? Note);

public sealed record CorelConnectionStep(string Method, bool Success, string Detail);

public sealed record CorelConnectionDiagnostic(
    int ExpectedVersionMajor,
    IReadOnlyList<CorelProcessInfo> Processes,
    IReadOnlyList<CorelConnectionStep> Steps,
    int? ConnectedVersionMajor,
    string? ConnectedVia)
{
    public bool Connected => ConnectedVersionMajor is not null;

    public string ToCompactText()
    {
        if (Connected)
            return $"已连接 CorelDRAW VersionMajor {ConnectedVersionMajor}（{ConnectedVia}）。";
        if (Processes.Count == 0)
            return "未发现 CorelDRW.exe 进程，也未取得 CorelDRAW COM 运行实例。";
        return $"检测到 {Processes.Count} 个 CorelDRW.exe 进程，但未取得 VersionMajor {ExpectedVersionMajor} 的 COM 运行实例。";
    }
}

public sealed class CorelRunningConnection : IDisposable
{
    private object? _application;

    internal CorelRunningConnection(object application, int actualVersionMajor, string method, CorelConnectionDiagnostic diagnostic)
    {
        _application = application;
        ActualVersionMajor = actualVersionMajor;
        Method = method;
        Diagnostic = diagnostic;
    }

    public object Application => _application ?? throw new ObjectDisposedException(nameof(CorelRunningConnection));
    public int ActualVersionMajor { get; }
    public string Method { get; }
    public CorelConnectionDiagnostic Diagnostic { get; }

    public void Dispose()
    {
        var application = Interlocked.Exchange(ref _application, null);
        ReleaseCom(application);
    }

    internal static void ReleaseCom(object? value)
    {
        if (value is null || !Marshal.IsComObject(value))
            return;
        try { Marshal.FinalReleaseComObject(value); }
        catch { }
    }
}

/// <summary>
/// Attaches only to an already-running CorelDRAW instance. It never creates or starts CorelDRAW.
/// Connection order: versioned/generic ProgID GetActiveObject, then Running Object Table enumeration.
/// </summary>
public static class CorelRunningInstanceConnector
{
    public static IEnumerable<string> CandidateProgIds(int versionMajor)
    {
        if (versionMajor > 0)
            yield return $"CorelDRAW.Application.{versionMajor}";
        yield return "CorelDRAW.Application";
    }

    public static CorelRunningConnection? TryConnect(int versionMajor, out CorelConnectionDiagnostic diagnostic)
    {
        var processes = DetectCorelProcesses();
        var steps = new List<CorelConnectionStep>();

        if (!OperatingSystem.IsWindows())
        {
            diagnostic = new CorelConnectionDiagnostic(versionMajor, processes, steps, null, null);
            return null;
        }

        foreach (var progId in CandidateProgIds(versionMajor))
        {
            object? application = null;
            try
            {
                var hrClsid = CLSIDFromProgID(progId, out var clsid);
                if (hrClsid != 0)
                {
                    steps.Add(new CorelConnectionStep(progId, false, $"CLSIDFromProgID 失败 0x{hrClsid:X8}"));
                    continue;
                }

                var hr = GetActiveObject(ref clsid, IntPtr.Zero, out application);
                if (hr != 0 || application is null)
                {
                    steps.Add(new CorelConnectionStep(progId, false, $"GetActiveObject 失败 0x{hr:X8}"));
                    CorelRunningConnection.ReleaseCom(application);
                    continue;
                }

                var actual = TryGetCorelVersionMajor(application);
                if (actual is null)
                {
                    steps.Add(new CorelConnectionStep(progId, false, "取得对象，但无法读取 CorelDRAW VersionMajor/FrameWork。"));
                    CorelRunningConnection.ReleaseCom(application);
                    continue;
                }
                if (versionMajor > 0 && actual.Value != versionMajor)
                {
                    steps.Add(new CorelConnectionStep(progId, false, $"取得的是 VersionMajor {actual.Value}，与目标 {versionMajor} 不一致。"));
                    CorelRunningConnection.ReleaseCom(application);
                    continue;
                }

                steps.Add(new CorelConnectionStep(progId, true, $"已取得 CorelDRAW VersionMajor {actual.Value}。"));
                diagnostic = new CorelConnectionDiagnostic(versionMajor, processes, steps.ToArray(), actual.Value, $"GetActiveObject:{progId}");
                return new CorelRunningConnection(application, actual.Value, $"GetActiveObject:{progId}", diagnostic);
            }
            catch (Exception ex)
            {
                steps.Add(new CorelConnectionStep(progId, false, $"异常：{Unwrap(ex).Message}"));
                CorelRunningConnection.ReleaseCom(application);
            }
        }

        var rotConnection = TryConnectFromRot(versionMajor, processes, steps, out diagnostic);
        if (rotConnection is not null)
            return rotConnection;

        diagnostic = new CorelConnectionDiagnostic(versionMajor, processes, steps.ToArray(), null, null);
        return null;
    }

    private static CorelRunningConnection? TryConnectFromRot(
        int versionMajor,
        IReadOnlyList<CorelProcessInfo> processes,
        List<CorelConnectionStep> steps,
        out CorelConnectionDiagnostic diagnostic)
    {
        IRunningObjectTable? rot = null;
        IEnumMoniker? enumMoniker = null;
        IBindCtx? bindCtx = null;
        var inspected = 0;
        var corelCandidates = 0;

        try
        {
            var hr = GetRunningObjectTable(0, out var runningObjectTable);
            rot = runningObjectTable;
            if (hr != 0 || rot is null)
            {
                steps.Add(new CorelConnectionStep("ROT", false, $"GetRunningObjectTable 失败 0x{hr:X8}"));
                diagnostic = new CorelConnectionDiagnostic(versionMajor, processes, steps.ToArray(), null, null);
                return null;
            }

            rot.EnumRunning(out var runningMonikerEnumerator);
            enumMoniker = runningMonikerEnumerator;
            var bindHr = CreateBindCtx(0, out var createdBindCtx);
            bindCtx = bindHr == 0 ? createdBindCtx : null;

            var monikers = new IMoniker[1];
            while (enumMoniker.Next(1, monikers, IntPtr.Zero) == 0 && inspected < 512)
            {
                inspected++;
                var moniker = monikers[0];
                object? candidate = null;
                string displayName = string.Empty;
                try
                {
                    if (bindCtx is not null)
                    {
                        try { moniker.GetDisplayName(bindCtx, null!, out displayName); }
                        catch { displayName = string.Empty; }
                    }

                    var getHr = rot.GetObject(moniker, out var runningObject);
                    candidate = runningObject;
                    if (getHr != 0 || candidate is null)
                        continue;

                    var actual = TryGetCorelVersionMajor(candidate);
                    if (actual is null)
                        continue;
                    corelCandidates++;
                    if (versionMajor > 0 && actual.Value != versionMajor)
                        continue;

                    var detail = string.IsNullOrWhiteSpace(displayName)
                        ? $"已枚举到 CorelDRAW VersionMajor {actual.Value}。"
                        : $"已枚举到 CorelDRAW VersionMajor {actual.Value} · {displayName}";
                    steps.Add(new CorelConnectionStep("ROT", true, detail));
                    diagnostic = new CorelConnectionDiagnostic(versionMajor, processes, steps.ToArray(), actual.Value, "ROT");
                    var owned = candidate;
                    candidate = null;
                    return new CorelRunningConnection(owned, actual.Value, "ROT", diagnostic);
                }
                catch
                {
                    // Ignore unrelated ROT entries and continue.
                }
                finally
                {
                    CorelRunningConnection.ReleaseCom(candidate);
                    CorelRunningConnection.ReleaseCom(moniker);
                    monikers[0] = null!;
                }
            }

            steps.Add(new CorelConnectionStep(
                "ROT",
                false,
                $"枚举 {inspected} 个 ROT 项；识别到 {corelCandidates} 个 CorelDRAW Application，但没有匹配 VersionMajor {versionMajor} 的对象。"));
        }
        catch (Exception ex)
        {
            steps.Add(new CorelConnectionStep("ROT", false, $"异常：{Unwrap(ex).Message}"));
        }
        finally
        {
            CorelRunningConnection.ReleaseCom(enumMoniker);
            CorelRunningConnection.ReleaseCom(bindCtx);
            CorelRunningConnection.ReleaseCom(rot);
        }

        diagnostic = new CorelConnectionDiagnostic(versionMajor, processes, steps.ToArray(), null, null);
        return null;
    }

    private static IReadOnlyList<CorelProcessInfo> DetectCorelProcesses()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<CorelProcessInfo>();

        var result = new List<CorelProcessInfo>();
        Process[] processes;
        try { processes = Process.GetProcessesByName("CorelDRW"); }
        catch { return result; }

        foreach (var process in processes)
        {
            using (process)
            {
                string? path = null;
                string? fileVersion = null;
                int? versionMajor = null;
                string? note = null;
                try
                {
                    path = process.MainModule?.FileName;
                    if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    {
                        fileVersion = FileVersionInfo.GetVersionInfo(path).FileVersion;
                        versionMajor = ParseVersionMajor(fileVersion);
                    }
                }
                catch (Exception ex)
                {
                    note = $"无法读取进程路径/版本：{ex.Message}";
                }

                result.Add(new CorelProcessInfo(process.Id, versionMajor, fileVersion, path, note));
            }
        }
        return result;
    }

    private static int? TryGetCorelVersionMajor(object application)
    {
        try
        {
            var type = application.GetType();
            var value = type.InvokeMember(
                "VersionMajor",
                BindingFlags.GetProperty,
                binder: null,
                target: application,
                args: null);
            if (value is null)
                return null;
            var version = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            if (version is < 14 or > 40)
                return null;

            object? frameWork = null;
            try
            {
                frameWork = type.InvokeMember(
                    "FrameWork",
                    BindingFlags.GetProperty,
                    binder: null,
                    target: application,
                    args: null);
                if (frameWork is null)
                    return null;
            }
            finally
            {
                CorelRunningConnection.ReleaseCom(frameWork);
            }
            return version;
        }
        catch
        {
            return null;
        }
    }

    private static int? ParseVersionMajor(string? fileVersion)
    {
        if (string.IsNullOrWhiteSpace(fileVersion))
            return null;
        var token = fileVersion.Split(new[] { '.', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(token, out var value) && value is >= 14 and <= 40 ? value : null;
    }

    private static Exception Unwrap(Exception ex)
    {
        while (ex.InnerException is not null)
            ex = ex.InnerException;
        return ex;
    }

    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int CLSIDFromProgID(string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = true)]
    private static extern int GetActiveObject(
        ref Guid rclsid,
        IntPtr reserved,
        [MarshalAs(UnmanagedType.IUnknown)] out object? application);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable runningObjectTable);

    [DllImport("ole32.dll", PreserveSig = true)]
    private static extern int CreateBindCtx(int reserved, out IBindCtx bindCtx);
}
