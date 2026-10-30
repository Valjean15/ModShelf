using System.Runtime.Versioning;

using Microsoft.Win32;

namespace ModShelf.Platform.Links;

/// <summary>
/// A tree of the Windows registry, under one of its keys, such as <c>HKEY_CURRENT_USER</c>.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsRegistryTree(RegistryKey root) : IRegistryTree
{
    public bool Exists(string path)
    {
        using RegistryKey? key = root.OpenSubKey(path);
        return key is not null;
    }

    public string? GetString(string path, string? name)
    {
        using RegistryKey? key = root.OpenSubKey(path);
        return key?.GetValue(name ?? string.Empty, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void SetString(string path, string? name, string value)
    {
        using RegistryKey key = root.CreateSubKey(path, writable: true);
        key.SetValue(name ?? string.Empty, value, RegistryValueKind.String);
    }

    public void CopyTree(string from, string to)
    {
        using RegistryKey source = root.OpenSubKey(from) ?? throw new IOException($"The registry key {from} doesn't exist.");
        using RegistryKey target = root.CreateSubKey(to, writable: true);
        Copy(source, target);
    }

    public void DeleteTree(string path) => root.DeleteSubKeyTree(path, throwOnMissingSubKey: false);

    public void DeleteIfEmpty(string path)
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
