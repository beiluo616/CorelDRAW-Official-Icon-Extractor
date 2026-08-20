using System.ComponentModel;
using System.Runtime.InteropServices;

namespace CDRIconExtractor.Windows.Resources;

public sealed record Win32ResourceBlob(ushort TypeId, string Name, ushort Language, byte[] Bytes);

public sealed record Win32ResourceTypeSummary(
    string TypeName,
    ushort? TypeId,
    int ResourceCount,
    long TotalBytes,
    IReadOnlyList<string> SampleNames);

public interface IWin32ResourceCatalog
{
    IReadOnlyList<Win32ResourceTypeSummary> InspectResourceTypes(string modulePath);
    IReadOnlyList<Win32ResourceBlob> ReadResources(string modulePath, string typeName);
}

public interface IWin32ResourceReader
{
    IReadOnlyList<Win32ResourceBlob> ReadResources(string modulePath, ushort typeId);
}

public sealed class Win32ResourceReader : IWin32ResourceReader, IWin32ResourceCatalog
{
    public IReadOnlyList<Win32ResourceBlob> ReadResources(string modulePath, ushort typeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        if (!File.Exists(modulePath))
            throw new FileNotFoundException("Resource module was not found.", modulePath);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Win32 resource extraction requires Windows.");

        // Resource-only data-file mapping is deliberately used instead of executable/image
        // loading. This keeps extraction read-only and also works for legacy 32-bit CorelDRAW
        // resource modules when the extractor itself is a 64-bit process.
        var module = NativeMethods.LoadLibraryExW(
            modulePath,
            IntPtr.Zero,
            NativeMethods.LOAD_LIBRARY_AS_DATAFILE);
        if (module == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to load resource module: {modulePath}");

        try
        {
            var results = new List<Win32ResourceBlob>();
            var typePtr = NativeMethods.MakeIntResource(typeId);
            var nameCallback = new NativeMethods.EnumResNameProc((h, type, name, _lParam) =>
            {
                var resourceName = NativeMethods.ResourceNameToString(name);
                var langCallback = new NativeMethods.EnumResLangProc((h2, type2, name2, language, _languageParam) =>
                {
                    var info = NativeMethods.FindResourceExW(h2, type2, name2, language);
                    if (info == IntPtr.Zero)
                        return true;

                    var size = NativeMethods.SizeofResource(h2, info);
                    if (size == 0)
                        return true;

                    var resource = NativeMethods.LoadResource(h2, info);
                    if (resource == IntPtr.Zero)
                        return true;
                    var pointer = NativeMethods.LockResource(resource);
                    if (pointer == IntPtr.Zero)
                        return true;

                    var bytes = new byte[checked((int)size)];
                    Marshal.Copy(pointer, bytes, 0, checked((int)size));
                    results.Add(new Win32ResourceBlob(typeId, resourceName, language, bytes));
                    return true;
                });

                NativeMethods.EnumResourceLanguagesW(h, type, name, langCallback, IntPtr.Zero);
                return true;
            });

            _ = NativeMethods.EnumResourceNamesW(module, typePtr, nameCallback, IntPtr.Zero);
            return results;
        }
        finally
        {
            _ = NativeMethods.FreeLibrary(module);
        }
    }

    public IReadOnlyList<Win32ResourceTypeSummary> InspectResourceTypes(string modulePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        if (!File.Exists(modulePath))
            throw new FileNotFoundException("Resource module was not found.", modulePath);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Win32 resource extraction requires Windows.");

        var module = NativeMethods.LoadLibraryExW(modulePath, IntPtr.Zero, NativeMethods.LOAD_LIBRARY_AS_DATAFILE);
        if (module == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to load resource module: {modulePath}");

        try
        {
            var summaries = new List<Win32ResourceTypeSummary>();
            var typeCallback = new NativeMethods.EnumResTypeProc((hModule, typePtr, _typeParam) =>
            {
                var typeName = NativeMethods.ResourceNameToString(typePtr);
                ushort? typeId = NativeMethods.IsIntResource(typePtr)
                    ? unchecked((ushort)typePtr.ToInt64())
                    : null;
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var count = 0;
                long totalBytes = 0;

                var nameCallback = new NativeMethods.EnumResNameProc((h, type, name, _nameParam) =>
                {
                    var resourceName = NativeMethods.ResourceNameToString(name);
                    if (names.Count < 8)
                        names.Add(resourceName);
                    var langCallback = new NativeMethods.EnumResLangProc((h2, type2, name2, language, _languageParam) =>
                    {
                        var info = NativeMethods.FindResourceExW(h2, type2, name2, language);
                        if (info == IntPtr.Zero)
                            return true;
                        var size = NativeMethods.SizeofResource(h2, info);
                        count++;
                        totalBytes += size;
                        return true;
                    });
                    _ = NativeMethods.EnumResourceLanguagesW(h, type, name, langCallback, IntPtr.Zero);
                    return true;
                });

                _ = NativeMethods.EnumResourceNamesW(hModule, typePtr, nameCallback, IntPtr.Zero);
                summaries.Add(new Win32ResourceTypeSummary(typeName, typeId, count, totalBytes, names.ToArray()));
                return true;
            });

            _ = NativeMethods.EnumResourceTypesW(module, typeCallback, IntPtr.Zero);
            return summaries
                .OrderByDescending(x => x.ResourceCount)
                .ThenBy(x => x.TypeName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        finally
        {
            _ = NativeMethods.FreeLibrary(module);
        }
    }


    public IReadOnlyList<Win32ResourceBlob> ReadResources(string modulePath, string typeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modulePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);
        if (!File.Exists(modulePath))
            throw new FileNotFoundException("Resource module was not found.", modulePath);
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Win32 resource extraction requires Windows.");

        var module = NativeMethods.LoadLibraryExW(modulePath, IntPtr.Zero, NativeMethods.LOAD_LIBRARY_AS_DATAFILE);
        if (module == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Unable to load resource module: {modulePath}");

        var typePtr = Marshal.StringToHGlobalUni(typeName);
        try
        {
            var results = new List<Win32ResourceBlob>();
            var nameCallback = new NativeMethods.EnumResNameProc((h, type, name, _lParam) =>
            {
                var resourceName = NativeMethods.ResourceNameToString(name);
                var langCallback = new NativeMethods.EnumResLangProc((h2, type2, name2, language, _languageParam) =>
                {
                    var info = NativeMethods.FindResourceExW(h2, type2, name2, language);
                    if (info == IntPtr.Zero)
                        return true;
                    var size = NativeMethods.SizeofResource(h2, info);
                    if (size == 0)
                        return true;
                    var resource = NativeMethods.LoadResource(h2, info);
                    if (resource == IntPtr.Zero)
                        return true;
                    var pointer = NativeMethods.LockResource(resource);
                    if (pointer == IntPtr.Zero)
                        return true;
                    var bytes = new byte[checked((int)size)];
                    Marshal.Copy(pointer, bytes, 0, checked((int)size));
                    results.Add(new Win32ResourceBlob(0, resourceName, language, bytes));
                    return true;
                });
                _ = NativeMethods.EnumResourceLanguagesW(h, type, name, langCallback, IntPtr.Zero);
                return true;
            });

            _ = NativeMethods.EnumResourceNamesW(module, typePtr, nameCallback, IntPtr.Zero);
            return results;
        }
        finally
        {
            Marshal.FreeHGlobal(typePtr);
            _ = NativeMethods.FreeLibrary(module);
        }
    }

}
