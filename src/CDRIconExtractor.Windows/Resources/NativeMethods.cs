using System.Runtime.InteropServices;

namespace CDRIconExtractor.Windows.Resources;

internal static class NativeMethods
{
    internal const uint LOAD_LIBRARY_AS_DATAFILE = 0x00000002;
    internal const uint LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x00000020;

    internal delegate bool EnumResTypeProc(IntPtr hModule, IntPtr lpszType, IntPtr lParam);
    internal delegate bool EnumResNameProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, IntPtr lParam);
    internal delegate bool EnumResLangProc(IntPtr hModule, IntPtr lpszType, IntPtr lpszName, ushort wIDLanguage, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr LoadLibraryExW(string lpFileName, IntPtr hFile, uint dwFlags);


    [DllImport("kernel32.dll", EntryPoint = "EnumResourceTypesW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumResourceTypesW(IntPtr hModule, EnumResTypeProc callback, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "EnumResourceNamesW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumResourceNamesW(IntPtr hModule, IntPtr lpType, EnumResNameProc callback, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "EnumResourceLanguagesW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumResourceLanguagesW(IntPtr hModule, IntPtr lpType, IntPtr lpName, EnumResLangProc callback, IntPtr lParam);

    [DllImport("kernel32.dll", EntryPoint = "FindResourceExW", CharSet = CharSet.Unicode, SetLastError = true)]
    internal static extern IntPtr FindResourceExW(IntPtr hModule, IntPtr lpType, IntPtr lpName, ushort wLanguage);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern uint SizeofResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LoadResource(IntPtr hModule, IntPtr hResInfo);

    [DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LockResource(IntPtr hResData);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool FreeLibrary(IntPtr hModule);

    internal static IntPtr MakeIntResource(ushort value) => (IntPtr)value;

    internal static bool IsIntResource(IntPtr value) => unchecked((ulong)value.ToInt64()) <= ushort.MaxValue;

    internal static string ResourceNameToString(IntPtr value) =>
        IsIntResource(value)
            ? unchecked((ushort)value.ToInt64()).ToString(System.Globalization.CultureInfo.InvariantCulture)
            : Marshal.PtrToStringUni(value) ?? string.Empty;
}
