using OpenWindows.Models;
using TS = Microsoft.Win32.TaskScheduler;

namespace OpenWindows.Services;

public sealed class TaskScanner
{
    public List<Finding> GetFindings()
    {
        var findings = new List<Finding>();

        TS.TaskService? taskService = null;
        try
        {
            taskService = new TS.TaskService();
        }
        catch (Exception ex)
        {
            Log.Write("Task scanner init failed: " + ex.Message);
            return findings;
        }

        try
        {
            foreach (var catalogItem in Catalogs.Tasks)
            {
                try
                {
                    var fullPath = catalogItem.Path.TrimEnd('\\') + "\\" + catalogItem.Name;
                    var task = taskService.GetTask(fullPath);
                    if (task == null)
                    {
                        continue;
                    }
                    if (!task.Definition.Settings.Enabled)
                    {
                        continue;
                    }

                    findings.Add(new Finding
                    {
                        Category = "Task",
                        Title = $"Task: {catalogItem.Name}",
                        Detail = $"{catalogItem.Why}. Disabling removes a small periodic background load.",
                        Risk = RiskLevel.Safe,
                        DefaultChecked = true,
                        NeedsAdmin = true,
                        Action = new ScheduledTaskAction(catalogItem.Path, catalogItem.Name)
                    });
                }
                catch
                {
                }
            }
        }
        finally
        {
            taskService.Dispose();
        }

        return findings;
    }
}
