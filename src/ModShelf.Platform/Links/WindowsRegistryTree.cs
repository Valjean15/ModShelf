using System.Runtime.Versioning;
using System.Security;

using Microsoft.Win32;

namespace ModShelf.Platform.Links;

/// <summary>
/// A tree of the Windows registry, under one of its keys, such as <c>HKEY_CURRENT_USER</c>. A key the user may not
/// open throws <see cref="UnauthorizedAccessException"/>, as a key the user may not change does.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsRegistryTree(RegistryKey root) : IRegistryTree
{
    public bool Exists(string path) => Guarded(() =>
    {
        using RegistryKey? key = root.OpenSubKey(path);
        return key is not null;
    });

    public string? GetString(string path, string? name) => Guarded(() =>
    {
        using RegistryKey? key = root.OpenSubKey(path);
        return key?.GetValue(name ?? string.Empty, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    });

    public void SetString(string path, string? name, string value) => Guarded(() =>
    {
        using RegistryKey key = root.CreateSubKey(path, writable: true);
        key.SetValue(name ?? string.Empty, value, RegistryValueKind.String);
        return true;
    });

    public void DeleteValue(string path, string name) => Guarded(() =>
    {
        using RegistryKey? key = root.OpenSubKey(path, writable: true);
        key?.DeleteValue(name, throwOnMissingValue: false);
        return true;
    });

    public void CopyTree(string from, string to) => Guarded(() =>
    {
        using RegistryKey source = root.OpenSubKey(from) ?? throw new IOException($"The registry key {from} doesn't exist.");
        using RegistryKey target = root.CreateSubKey(to, writable: true);
        Copy(source, target);
        return true;
    });

    public void DeleteTree(string path) => Guarded(() =>
    {
        root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
        return true;
    });

    public void DeleteIfEmpty(string path) => Guarded(() =>
    {
        bool empty;
        using (RegistryKey? key = root.OpenSubKey(path))
        {
            empty = key is { ValueCount: 0, SubKeyCount: 0 };
        }

        if (empty)
        {
            root.DeleteSubKey(path, throwOnMissingSubKey: false);
        }

        return true;
    });

    // Opening a key the user may not read throws SecurityException, which the port doesn't name.
    private static T Guarded<T>(Func<T> work)
    {
        try
        {
            return work();
        }
        catch (SecurityException exception)
        {
            throw new UnauthorizedAccessException(exception.Message, exception);
        }
    }

    private static void Copy(RegistryKey source, RegistryKey target)
    {
        foreach (string name in source.GetValueNames())
        {
            target.SetValue(
                name,
                source.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)!,
                source.GetValueKind(name));
        }

        foreach (string name in source.GetSubKeyNames())
        {
            using RegistryKey from = source.OpenSubKey(name)!;
            using RegistryKey to = target.CreateSubKey(name, writable: true);
            Copy(from, to);
        }
    }
}
