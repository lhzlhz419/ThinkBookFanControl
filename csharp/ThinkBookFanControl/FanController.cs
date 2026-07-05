using System;
using System.Collections.Generic;
using System.Linq;
using System.Management;

namespace ThinkBookFanControl;

public sealed class FanController
{
    private const string NamespacePath = @"root\wmi";
    private const uint FullSpeedId = 0x04020000;
    private const uint Fan1Id = 0x04030001;
    private const uint Fan2Id = 0x04030002;
    private IReadOnlyDictionary<string, FanLimit>? _cachedLimits;

    public FanSnapshot ReadSnapshot()
    {
        using var other = GetActiveOtherMethod();
        var fan1 = ReadFeatureValue(other, Fan1Id);
        var fan2 = ReadFeatureValue(other, Fan2Id);
        var limits = _cachedLimits ??= ReadFanLimits();
        return new FanSnapshot(DateTimeOffset.Now, fan1, fan2, limits);
    }

    public void ApplyBoth(int rpm)
    {
        Apply(rpm, rpm);
    }

    public void Apply(int fan1Rpm, int fan2Rpm)
    {
        using var other = GetActiveOtherMethod();
        SetFeatureValue(other, Fan1Id, fan1Rpm);
        SetFeatureValue(other, Fan2Id, fan2Rpm);
    }

    public void RestoreAuto()
    {
        ApplyBoth(0);
    }

    public void SetFullSpeed(bool enabled)
    {
        using var other = GetActiveOtherMethod();
        SetFeatureValue(other, FullSpeedId, enabled ? 1 : 0);
    }

    public static int ClampForBoth(int rpm, IReadOnlyDictionary<string, FanLimit> limits)
    {
        var minimum = Math.Max(limits["fan1"].MinRpm, limits["fan2"].MinRpm);
        var maximum = Math.Min(limits["fan1"].MaxRpm, limits["fan2"].MaxRpm);
        return Math.Max(minimum, Math.Min(maximum, rpm));
    }

    public static (int MinRpm, int MaxRpm) SharedRange(IReadOnlyDictionary<string, FanLimit> limits)
    {
        return (
            Math.Max(limits["fan1"].MinRpm, limits["fan2"].MinRpm),
            Math.Min(limits["fan1"].MaxRpm, limits["fan2"].MaxRpm));
    }

    private static ManagementObject GetActiveOtherMethod()
    {
        using var searcher = new ManagementObjectSearcher(NamespacePath, "SELECT * FROM LENOVO_OTHER_METHOD");
        foreach (ManagementObject item in searcher.Get())
        {
            if (IsActive(item))
                return item;
            item.Dispose();
        }

        throw new InvalidOperationException("No active LENOVO_OTHER_METHOD instance found.");
    }

    private static IReadOnlyDictionary<string, FanLimit> ReadFanLimits()
    {
        using var searcher = new ManagementObjectSearcher(NamespacePath, "SELECT * FROM LENOVO_FAN_TEST_DATA");
        foreach (ManagementObject item in searcher.Get())
        {
            using (item)
            {
                if (!IsActive(item))
                    continue;

                var ids = ToIntArray(item["FanId"]);
                var mins = ToIntArray(item["FanMinSpeed"]);
                var maxes = ToIntArray(item["FanMaxSpeed"]);
                if (ids.Length < 2 || mins.Length < 2 || maxes.Length < 2)
                    break;

                return new Dictionary<string, FanLimit>
                {
                    ["fan1"] = new("fan1", Fan1Id, mins[0], maxes[0]),
                    ["fan2"] = new("fan2", Fan2Id, mins[1], maxes[1]),
                };
            }
        }

        throw new InvalidOperationException("No active LENOVO_FAN_TEST_DATA instance found.");
    }

    private static int ReadFeatureValue(ManagementObject other, uint id)
    {
        var errors = new List<string>();

        try
        {
            var args = new object?[] { id, null };
            other.InvokeMethod("GetFeatureValue", args);
            if (args.Length > 1 && args[1] is not null)
                return Convert.ToInt32(args[1]);
            errors.Add("positional [id, out value] returned no out value");
        }
        catch (Exception ex)
        {
            errors.Add("positional [id, out value]: " + DescribeException(ex));
        }

        try
        {
            var args = new object?[] { id };
            var result = other.InvokeMethod("GetFeatureValue", args);
            if (result is not null)
                return Convert.ToInt32(result);
            errors.Add("positional [id] returned null");
        }
        catch (Exception ex)
        {
            errors.Add("positional [id]: " + DescribeException(ex));
        }

        try
        {
            using var inParams = other.GetMethodParameters("GetFeatureValue");
            SetParameter(inParams, ["Data", "Id", "ID", "FeatureId", "AttributeId"], id);
            using var outParams = other.InvokeMethod("GetFeatureValue", inParams, null);
            if (TryGetParameter(outParams, ["value", "Value", "Data"], out var value))
                return Convert.ToInt32(value);
            errors.Add("named parameters returned no value");
        }
        catch (Exception ex)
        {
            errors.Add("named parameters: " + DescribeException(ex));
        }

        throw new InvalidOperationException($"GetFeatureValue(0x{id:X8}) failed. " + string.Join(" | ", errors));
    }

    private static void SetFeatureValue(ManagementObject other, uint id, int value)
    {
        var errors = new List<string>();

        try
        {
            var args = new object?[] { id, unchecked((uint)value) };
            _ = other.InvokeMethod("SetFeatureValue", args);
            return;
        }
        catch (Exception ex)
        {
            errors.Add("positional [id, value]: " + DescribeException(ex));
        }

        try
        {
            using var inParams = other.GetMethodParameters("SetFeatureValue");
            SetParameter(inParams, ["IDs", "Data", "Id", "ID", "FeatureId", "AttributeId"], id);
            SetParameter(inParams, ["value", "Value"], unchecked((uint)value));
            using var outParams = other.InvokeMethod("SetFeatureValue", inParams, null);
            return;
        }
        catch (Exception ex)
        {
            errors.Add("named parameters: " + DescribeException(ex));
        }

        throw new InvalidOperationException($"SetFeatureValue(0x{id:X8}, {value}) failed. " + string.Join(" | ", errors));
    }

    private static void SetParameter(ManagementBaseObject parameters, string[] names, object value)
    {
        foreach (var name in names)
        {
            if (parameters.Properties[name] is not null)
            {
                parameters[name] = value;
                return;
            }
        }

        var available = string.Join(", ", parameters.Properties.Cast<PropertyData>().Select(property => property.Name));
        throw new InvalidOperationException($"None of the WMI parameters [{string.Join(", ", names)}] exist. Available: {available}");
    }

    private static bool TryGetParameter(ManagementBaseObject parameters, string[] names, out object? value)
    {
        foreach (var name in names)
        {
            if (parameters.Properties[name] is not null)
            {
                value = parameters[name];
                return value is not null;
            }
        }

        value = null;
        return false;
    }

    private static string DescribeException(Exception exception)
    {
        return exception is ManagementException managementException
            ? $"{exception.GetType().Name}({managementException.ErrorCode}): {exception.Message}"
            : $"{exception.GetType().Name}: {exception.Message}";
    }

    private static bool IsActive(ManagementBaseObject item)
    {
        return item.Properties["Active"] is null || Convert.ToBoolean(item["Active"]);
    }

    private static int[] ToIntArray(object? value)
    {
        if (value is Array array)
            return array.Cast<object>().Select(Convert.ToInt32).ToArray();
        return [];
    }
}
