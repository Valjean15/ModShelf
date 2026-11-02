namespace ModShelf.Presentation.ViewModels.Downloads;

/// <summary>
/// The downloads a tab of the Downloads screen shows.
/// </summary>
public enum DownloadTab
{
    /// <summary>Every download.</summary>
    All,

    /// <summary>Queued, getting their link, downloading, being checked, or paused.</summary>
    UnderWay,

    /// <summary>Waiting for the user to start them on their site.</summary>
    WaitingForYou,

    /// <summary>Failed, to retry.</summary>
    Failed,

    /// <summary>Done or cancelled, to clear.</summary>
    Finished,
}
