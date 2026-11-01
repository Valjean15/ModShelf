using ModShelf.Cli.Common;

namespace ModShelf.Cli.Mcp;

/// <summary>
/// Every setting, where they're kept, and what each one's value can be.
/// </summary>
internal sealed record SettingsListing(string Location, IReadOnlyList<SettingValue> Settings, string Values);
