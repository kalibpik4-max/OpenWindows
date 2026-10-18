using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;

namespace OpenWindows.Services;

public static class ServiceControl
{
    private const uint ScManagerConnect = 0x0001;
    private const uint ServiceChangeConfig = 0x0002;
    private const uint ServiceNoChange = 0xFFFFFFFF;

    private const uint ServiceAutoStart = 2;
    private const uint ServiceDemandStart = 3;
    private const uint ServiceDisabled = 4;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenSCManager(string? machineName, string? databaseName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern IntPtr OpenService(IntPtr scManager, string serviceName, uint desiredAccess);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool ChangeServiceConfig(
        IntPtr service,
        uint serviceType,
        uint startType,
        uint errorControl,
        string? binaryPathName,
        string? loadOrderGroup,
        IntPtr tagId,
        string[]? dependencies,
        string? serviceStartName,
        string? password,
        string? displayName);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr handle);

    public static void SetStartType(string serviceName, ServiceStartMode mode)
    {
        var startType = mode switch
        {
            ServiceStartMode.Automatic => ServiceAutoStart,
            ServiceStartMode.Manual => ServiceDemandStart,
            ServiceStartMode.Disabled => ServiceDisabled,
            _ => throw new InvalidOperationException("Unsupported start mode: " + mode)
        };

        var scManager = OpenSCManager(null, null, ScManagerConnect);
        if (scManager == IntPtr.Zero)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenSCManager failed");
        }
        try
        {
            var service = OpenService(scManager, serviceName, ServiceChangeConfig);
            if (service == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), $"OpenService failed for {serviceName}");
            }
            try
            {
                var ok = ChangeServiceConfig(service, ServiceNoChange, startType, ServiceNoChange,
                    null, null, IntPtr.Zero, null, null, null, null);
                if (!ok)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), $"ChangeServiceConfig failed for {serviceName}");
                }
            }
            finally
            {
                CloseServiceHandle(service);
            }
        }
        finally
        {
            CloseServiceHandle(scManager);
        }
    }

    public static ServiceStartMode GetStartType(string serviceName)
    {
        var controller = new ServiceController(serviceName);
        return controller.StartType;
    }

    public static bool IsRunning(string serviceName)
    {
        try
        {
            var controller = new ServiceController(serviceName);
            return controller.Status == ServiceControllerStatus.Running;
        }
        catch
        {
            return false;
        }
    }

    public static bool TryStop(string serviceName, int timeoutMs = 10000)
    {
        try
        {
            var controller = new ServiceController(serviceName);
            if (controller.Status == ServiceControllerStatus.Stopped)
            {
                return true;
            }
            controller.Stop();
            try
            {
                controller.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromMilliseconds(timeoutMs));
            }
            catch
            {
                return controller.Status == ServiceControllerStatus.Stopped;
            }
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"Service stop failed for {serviceName}: {ex.Message}");
            return false;
        }
    }

    public static bool TryStart(string serviceName)
    {
        try
        {
            var controller = new ServiceController(serviceName);
            if (controller.Status == ServiceControllerStatus.Running)
            {
                return true;
            }
            controller.Start();
            return true;
        }
        catch (Exception ex)
        {
            Log.Write($"Service start failed for {serviceName}: {ex.Message}");
            return false;
        }
    }

    public static string StartModeToString(ServiceStartMode mode) => mode switch
    {
        ServiceStartMode.Automatic => "Auto",
        ServiceStartMode.Manual => "Manual",
        ServiceStartMode.Disabled => "Disabled",
        ServiceStartMode.Boot => "Boot",
        ServiceStartMode.System => "System",
        _ => mode.ToString()
    };

    public static ServiceStartMode ParseStartMode(string? text)
    {
        return text switch
        {
            "Auto" or "Automatic" or "auto" => ServiceStartMode.Automatic,
            "Disabled" or "disabled" => ServiceStartMode.Disabled,
            "Manual" or "manual" => ServiceStartMode.Manual,
            "Boot" => ServiceStartMode.Boot,
            "System" => ServiceStartMode.System,
            _ => ServiceStartMode.Manual
        };
    }
}
