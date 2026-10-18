namespace OpenWindows.Models;

public enum RiskLevel
{
    Safe,
    Caution,
    Info
}

public abstract record FindingAction;

public sealed record RegRemoveAction(string Hive, string SubKey, string Name) : FindingAction;

public sealed record RegSetAction(string Hive, string SubKey, string Name, object? Value, string Kind) : FindingAction;

public sealed record ServiceAction(string Name, string TargetStartType, bool Stop) : FindingAction;

public sealed record ScheduledTaskAction(string Path, string Name) : FindingAction;

public sealed record CleanFolderAction(IReadOnlyList<string> Paths, string Pattern) : FindingAction;

public sealed record ClearRecycleBinAction : FindingAction;

public sealed record PowerPlanAction(string Guid) : FindingAction;

public sealed record KillProcessAction(int Id, string Name) : FindingAction;

public sealed record MoveFileAction(string Source) : FindingAction;

public sealed record OpenDiskCleanupAction : FindingAction;

public sealed class Finding
{
    public required string Category { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public RiskLevel Risk { get; init; }
    public bool DefaultChecked { get; init; }
    public bool NeedsAdmin { get; init; }
    public FindingAction? Action { get; init; }

    public string CategoryLabel => Category switch
    {
        "Startup" => "Startup",
        "Service" => "Service",
        "Task" => "Task",
        "Tweaks" => "Tweak",
        "Cleanup" => "Cleanup",
        "Now" => "Right now",
        "System" => "System",
        _ => Category
    };

    public string RiskLabel => Risk.ToString();
}
