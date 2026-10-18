using OpenWindows.Models;

namespace OpenWindows.Services;

public sealed class ScanCoordinator
{
    public sealed record ScanProgressInfo(string StepName, int StepIndex, int StepCount);

    private readonly SystemInfoScanner systemInfo = new();
    private readonly StartupScanner startup = new();
    private readonly ServiceScanner services = new();
    private readonly TaskScanner tasks = new();
    private readonly TweakScanner tweaks = new();
    private readonly CleanupScanner cleanup = new();
    private readonly LoadScanner load = new();

    public async Task<List<Finding>> ScanAllAsync(
        IProgress<ScanProgressInfo>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var steps = new (string Name, Func<List<Finding>> Fn)[]
        {
            ("System info", systemInfo.GetFindings),
            ("Startup items", startup.GetFindings),
            ("Services", services.GetFindings),
            ("Scheduled tasks", tasks.GetFindings),
            ("Windows tweaks", tweaks.GetFindings),
            ("Disk cleanup", cleanup.GetFindings),
            ("Current load", load.GetFindings)
        };

        var results = new List<Finding>();
        for (var i = 0; i < steps.Length; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new ScanProgressInfo(steps[i].Name, i + 1, steps.Length));
            var findings = await Task.Run(steps[i].Fn, cancellationToken).ConfigureAwait(false);
            results.AddRange(findings);
        }
        return results;
    }
}
