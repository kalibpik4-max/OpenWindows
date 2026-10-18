using System.Runtime.InteropServices;

namespace OpenWindows;

public static class Program
{
    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int dwProcessId);

    private const int AttachParentProcess = -1;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Contains("--selftest", StringComparer.OrdinalIgnoreCase))
        {
            AttachConsoleAndRedirect();
            return SelfTest.Run();
        }

        if (args.Contains("--console", StringComparer.OrdinalIgnoreCase) ||
            args.Contains("-c", StringComparer.OrdinalIgnoreCase))
        {
            AttachConsoleAndRedirect();
            ConsoleMode.Run();
            return 0;
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }

    private static void AttachConsoleAndRedirect()
    {
        try
        {
            AttachConsole(AttachParentProcess);
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            Console.SetOut(stdout);
            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetError(stderr);
        }
        catch
        {
        }
    }
}
