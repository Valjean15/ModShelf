using ModShelf.Platform.Links;

namespace ModShelf.Platform.Tests.Links;

/// <summary>
/// A registry hive in memory: keys by path, ignoring case as Windows does, each with its values by name ("" for the
/// default value).
/// </summary>
internal sealed class MemoryRegistryTree : IRegistryTree
{
    private readonly Dictionary<string, Dictionary<string, object>> _keys = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Each key's path, as the test can compare them.
    /// </summary>
    public IReadOnlyCollection<string> Keys => _keys.Keys;

    /// <summary>
    /// A key's values, by name; the default value's name is empty.
    /// </summary>
    public IReadOnlyDictionary<string, object> ValuesOf(string path) => _keys.TryGetValue(path, out Dictionary<string, object>? values) ? values : new Dictionary<string, object>();

    /// <summary>
    /// Sets a value of any kind, as another app would, creating the key.
    /// </summary>
    public void Set(string path, string? name, object value) => Create(path)[name ?? string.Empty] = value;

    public bool Exists(string path) => _keys.ContainsKey(path);

    public string? GetString(string path, string? name)
        => _keys.TryGetValue(path, out Dictionary<string, object>? values) && values.TryGetValue(name ?? string.Empty, out object? value) ? value as string : null;

    public void SetString(string path, string? name, string value) => Set(path, name, value);

    public void CopyTree(string from, string to)
    {
        if (!Exists(from))
        {
            throw new IOException($"The registry key {from} doesn't exist.");
        }

        foreach (string key in Under(from).ToList())
        {
            Dictionary<string, object> copy = Create(to + key[from.Length..]);
            foreach (KeyValuePair<string, object> value in _keys[key])
            {
                copy[value.Key] = value.Value;
            }
        }
    }

    public void DeleteTree(string path)
    {
        foreach (string key in Under(path).ToList())
        {
            _keys.Remove(key);
        }
    }

    public void DeleteIfEmpty(string path)
    {
        if (_keys.TryGetValue(path, out Dictionary<string, object>? values) && values.Count == 0 && Under(path).Count() == 1)
        {
            _keys.Remove(path);
        }
    }

    private IEnumerable<string> Under(string path)
        => _keys.Keys.Where(key => key.Equals(path, StringComparison.OrdinalIgnoreCase) || key.StartsWith(path + @"\", StringComparison.OrdinalIgnoreCase));

    private Dictionary<string, object> Create(string path)
    {
        // Its parents exist too, as in the registry.
        string[] names = path.Split('\\');
        for (int count = 1; count < names.Length; count++)
        {
            _keys.TryAdd(string.Join('\\', names[..count]), []);
        }

        if (!_keys.TryGetValue(path, out Dictionary<string, object>? values))
        {
            values = [];
            _keys[path] = values;
        }

        return values;
    }
}
