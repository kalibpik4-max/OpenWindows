using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenWindows.Models;

public sealed class UndoEntry
{
    public string Type { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Hive { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SubKey { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? OldExists { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? OldValue { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OldKind { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OldStartType { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? WasRunning { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Source { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Dest { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OldGuid { get; set; }
}

public sealed record FixOutcome(Finding Finding, string Status, string Note);

public sealed class FixResult
{
    public int Applied { get; init; }
    public int Skipped { get; init; }
    public int Failed { get; init; }
    public string? BackupPath { get; init; }
    public IReadOnlyList<FixOutcome> Outcomes { get; init; } = Array.Empty<FixOutcome>();

    public string Summary
    {
        get
        {
            var parts = new List<string> { $"Applied {Applied}" };
            if (Skipped > 0) parts.Add($"skipped {Skipped}");
            if (Failed > 0) parts.Add($"failed {Failed}");
            var text = string.Join(", ", parts) + ".";
            if (BackupPath != null) text += " Undo is available.";
            return text;
        }
    }
}

public sealed class UndoResult
{
    public int Count { get; init; }
    public int Failed { get; init; }
    public string FileName { get; init; } = "";
    public string? Note { get; init; }
}

public sealed class FixProgress
{
    public required int Index { get; init; }
    public required int Total { get; init; }
    public required string Title { get; init; }
}
