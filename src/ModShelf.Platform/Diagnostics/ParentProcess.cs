using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace ModShelf.Platform.Diagnostics;

/// <summary>
/// The process that started this one, such as the agent's app that started the command line's MCP server.
/// </summary>
internal static partial class ParentProcess
{
    private const int ProcessBasicInformationClass = 0;

    // GetCurrentProcess's pseudo handle: this process, with nothing to close.
    private const nint CurrentProcess = -1;

    /// <returns>The parent's ID, or null when the system doesn't say.</returns>
    public static int? FindId()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                return NtQueryInformationProcess(CurrentProcess, ProcessBasicInformationClass, out BasicInformation information, Marshal.SizeOf<BasicInformation>(), out _) == 0
                    ? checked((int)information.InheritedFromUniqueProcessId)
                    : null;
            }

            return OperatingSystem.IsLinux() ? FromProcStat(File.ReadAllText("/proc/self/stat")) : GetParentProcessId();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DllNotFoundException or EntryPointNotFoundException or OverflowException)
        {
            return null;
        }
    }

    /// <returns>The name of the process with <paramref name="id"/>, or null when it has ended.</returns>
    public static string? NameOf(int id)
    {
        try
        {
            using Process process = Process.GetProcessById(id);
            return process.ProcessName;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// The parent's ID in a line of <c>/proc/&lt;pid&gt;/stat</c>: its fourth field, after the name in brackets, which may
    /// hold spaces and brackets itself.
    /// </summary>
    internal static int? FromProcStat(string stat)
    {
        int end = stat.LastIndexOf(')');
        if (end < 0)
        {
            return null;
        }

        string[] fields = stat[(end + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return fields.Length > 1 && int.TryParse(fields[1], NumberStyles.None, CultureInfo.InvariantCulture, out int parent) ? parent : null;
    }

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(nint process, int informationClass, out BasicInformation information, int length, out int returned);

    // macOS; on Linux, /proc says it without a library to find.
    [LibraryImport("libc", EntryPoint = "getppid")]
    private static partial int GetParentProcessId();

    /// <summary>
    /// Windows' PROCESS_BASIC_INFORMATION.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BasicInformation
    {
        public nint ExitStatus;
        public nint PebBaseAddress;
        public nint AffinityMask;
        public nint BasePriority;
        public nint UniqueProcessId;
        public nint InheritedFromUniqueProcessId;
    }
}
