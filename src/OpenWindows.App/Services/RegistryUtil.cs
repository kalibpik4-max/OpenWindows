using Microsoft.Win32;

namespace OpenWindows.Services;

public sealed record RegistryValueInfo(bool Exists, object? Value, RegistryValueKind? Kind);

public static class RegistryUtil
{
    public static RegistryKey? GetRoot(string hive) => hive.ToUpperInvariant() switch
    {
        "HKCU" => Registry.CurrentUser,
        "HKLM" => Registry.LocalMachine,
        _ => null
    };

    public static RegistryKey? OpenKey(string hive, string subKey, bool writable)
    {
        var root = GetRoot(hive);
        return root?.OpenSubKey(subKey, writable);
    }

    public static RegistryValueInfo GetValueInfo(string hive, string subKey, string name)
    {
        try
        {
            using var key = OpenKey(hive, subKey, false);
            if (key == null)
            {
                return new RegistryValueInfo(false, null, null);
            }
            var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (value == null)
            {
                var names = key.GetValueNames();
                if (!names.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return new RegistryValueInfo(false, null, null);
                }
            }
            var kind = key.GetValueKind(name);
            return new RegistryValueInfo(true, value, kind);
        }
        catch
        {
            return new RegistryValueInfo(false, null, null);
        }
    }

    public static void SetValue(string hive, string subKey, string name, object? value, RegistryValueKind kind)
    {
        using var key = RegistryKeyForWrite(hive, subKey);
        key.SetValue(name, value ?? "", kind);
    }

    public static bool RemoveValue(string hive, string subKey, string name)
    {
        using var key = OpenKey(hive, subKey, true);
        if (key == null)
        {
            return false;
        }
        var info = GetValueInfo(hive, subKey, name);
        if (!info.Exists)
        {
            return false;
        }
        key.DeleteValue(name, false);
        return true;
    }

    public static IReadOnlyList<KeyValuePair<string, object?>> EnumerateValues(string hive, string subKey)
    {
        var list = new List<KeyValuePair<string, object?>>();
        using var key = OpenKey(hive, subKey, false);
        if (key == null)
        {
            return list;
        }
        foreach (var name in key.GetValueNames())
        {
            try
            {
                var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                list.Add(new KeyValuePair<string, object?>(name, value));
            }
            catch
            {
            }
        }
        return list;
    }

    private static RegistryKey RegistryKeyForWrite(string hive, string subKey)
    {
        var root = GetRoot(hive) ?? throw new InvalidOperationException("Unknown registry hive: " + hive);
        return root.CreateSubKey(subKey, true);
    }

    public static RegistryValueKind ParseKind(string? kind)
    {
        if (kind != null && Enum.TryParse<RegistryValueKind>(kind, true, out var parsed))
        {
            return parsed;
        }
        return RegistryValueKind.String;
    }

    public static string KindToString(RegistryValueKind kind) => kind.ToString();

    public static object? ConvertJsonToRegistryValue(System.Text.Json.JsonElement? element, RegistryValueKind kind)
    {
        if (element == null || element.Value.ValueKind == System.Text.Json.JsonValueKind.Null)
        {
            return null;
        }
        var e = element.Value;
        try
        {
            return kind switch
            {
                RegistryValueKind.DWord => e.ValueKind == System.Text.Json.JsonValueKind.Number ? e.GetInt32() : int.TryParse(e.ToString(), out var i) ? i : 0,
                RegistryValueKind.QWord => e.ValueKind == System.Text.Json.JsonValueKind.Number ? e.GetInt64() : long.TryParse(e.ToString(), out var l) ? l : 0L,
                RegistryValueKind.MultiString => e.ValueKind == System.Text.Json.JsonValueKind.Array
                    ? e.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                    : new[] { e.ToString() },
                _ => e.ValueKind == System.Text.Json.JsonValueKind.String ? e.GetString() : e.ToString()
            };
        }
        catch
        {
            return e.ToString();
        }
    }
}
