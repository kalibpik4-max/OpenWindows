using System.ServiceProcess;
using OpenWindows.Models;

namespace OpenWindows.Services;

public sealed class ServiceScanner
{
    public List<Finding> GetFindings()
    {
        var findings = new List<Finding>();

        ServiceController[] services;
        try
        {
            services = ServiceController.GetServices();
        }
        catch
        {
            return findings;
        }

        var map = new Dictionary<string, ServiceController>(StringComparer.OrdinalIgnoreCase);
        foreach (var service in services)
        {
            map[service.ServiceName] = service;
        }

        foreach (var catalogItem in Catalogs.Services)
        {
            if (!map.TryGetValue(catalogItem.Name, out var service))
            {
                continue;
            }

            string startMode;
            string state;
            try
            {
                startMode = ServiceControl.StartModeToString(service.StartType);
                state = service.Status == ServiceControllerStatus.Running ? "Running" : "Stopped";
            }
            catch
            {
                continue;
            }

            if (startMode == catalogItem.Target)
            {
                continue;
            }

            findings.Add(new Finding
            {
                Category = "Service",
                Title = $"Service: {catalogItem.Name}",
                Detail = $"{catalogItem.Why}. Currently {startMode} ({state}).",
                Risk = catalogItem.Risk,
                DefaultChecked = catalogItem.Risk == RiskLevel.Safe,
                NeedsAdmin = true,
                Action = new ServiceAction(catalogItem.Name, catalogItem.Target, true)
            });
        }

        return findings;
    }
}
