using OpenWindows.Services;

namespace OpenWindows;

public static class ConsoleMode
{
    public static void Run()
    {
        try
        {
            var findings = AppServices.Scanner.ScanAllAsync().GetAwaiter().GetResult();

            var order = new[]
            {
                ("System info", "System"),
                ("Startup items", "Startup"),
                ("Services", "Service"),
                ("Scheduled tasks", "Task"),
                ("Windows tweaks", "Tweaks"),
                ("Disk cleanup", "Cleanup"),
                ("Current load", "Now")
            };

            foreach (var (header, category) in order)
            {
                Console.WriteLine("--- " + header + " ---");
                foreach (var f in findings.Where(x => x.Category == category))
                {
                    Console.WriteLine($"{"      ",0}{f.Risk,-8} | {f.Title} | {f.Detail}");
                }
            }

            Console.WriteLine("================");
            Console.WriteLine($"Total findings: {findings.Count}");
            Console.WriteLine("================");
            foreach (var f in findings)
            {
                Console.WriteLine($"{f.Risk,-8} | {f.Category,-8} | {f.Title} | {f.Detail}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("Scan failed: " + ex);
        }
    }
}
