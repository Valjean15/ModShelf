using ModShelf.Application.Settings;
using ModShelf.Presentation.Services;

namespace ModShelf.Presentation.Tests.Fakes;

/// <summary>
/// Records each theme applied, oldest first.
/// </summary>
internal sealed class RecordingThemeSwitcher : IThemeSwitcher
{
    public List<AppTheme> Applied { get; } = [];

    public void Apply(AppTheme theme) => Applied.Add(theme);
}
