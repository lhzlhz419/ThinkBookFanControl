using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ThinkBookFanControl;

public static class CurveProfileStore
{
    public static readonly int[] CpuTemps = Enumerable.Range(0, 15).Select(i => 30 + i * 5).ToArray();
    public static readonly int[] GpuTemps = Enumerable.Range(0, 13).Select(i => 30 + i * 5).ToArray();

    private const int ProfileCount = 5;
    private const int FallbackMinRpm = 1500;
    private const int FallbackMaxRpm = 5500;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string ProfilePath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".thinkbook_fan_control", "fan_curve_profiles.csharp.json");
        }
    }

    public static string SettingsPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(home, ".thinkbook_fan_control", "app_settings.csharp.json");
        }
    }

    public static List<FanProfile> Load()
    {
        var defaults = Defaults();
        if (!File.Exists(ProfilePath))
            return defaults;

        try
        {
            var loaded = JsonSerializer.Deserialize<List<FanProfile>>(File.ReadAllText(ProfilePath), JsonOptions);
            if (loaded is null)
                return defaults;

            for (var i = 0; i < Math.Min(ProfileCount, loaded.Count); i++)
            {
                defaults[i].Name = string.IsNullOrWhiteSpace(loaded[i].Name) ? $"Profile {i + 1}" : loaded[i].Name;
                defaults[i].TemperatureSmoothing = NormalizeSmoothingSamples(loaded[i].TemperatureSmoothing, defaults[i].TemperatureSmoothing);
                defaults[i].RampDownRpmPerSecond = PickAllowed(loaded[i].RampDownRpmPerSecond, [0, 10, 20, 50, 100], defaults[i].RampDownRpmPerSecond);
                defaults[i].CpuFan1Curve = NormalizeProfileCurve(loaded[i].CpuFan1Curve, loaded[i].CpuCurve, CpuTemps.Length, defaults[i].CpuFan1Curve);
                defaults[i].CpuFan2Curve = NormalizeProfileCurve(loaded[i].CpuFan2Curve, loaded[i].CpuCurve, CpuTemps.Length, defaults[i].CpuFan2Curve);
                defaults[i].GpuFan1Curve = NormalizeProfileCurve(loaded[i].GpuFan1Curve, loaded[i].GpuCurve, GpuTemps.Length, defaults[i].GpuFan1Curve);
                defaults[i].GpuFan2Curve = NormalizeProfileCurve(loaded[i].GpuFan2Curve, loaded[i].GpuCurve, GpuTemps.Length, defaults[i].GpuFan2Curve);
                defaults[i].CpuCurve = [.. defaults[i].CpuFan1Curve];
                defaults[i].GpuCurve = [.. defaults[i].GpuFan1Curve];
            }
            return defaults;
        }
        catch
        {
            return defaults;
        }
    }

    public static void Save(IReadOnlyList<FanProfile> profiles)
    {
        var directory = Path.GetDirectoryName(ProfilePath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(ProfilePath, JsonSerializer.Serialize(profiles, JsonOptions));
    }

    public static AppSettings LoadSettings()
    {
        var defaults = new AppSettings();
        if (!File.Exists(SettingsPath))
            return defaults;

        try
        {
            var loaded = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath), JsonOptions);
            if (loaded is null)
                return defaults;

            defaults.Language = loaded.Language is "en-US" or "zh-CN" ? loaded.Language : defaults.Language;
            defaults.Theme = loaded.Theme is "dark" or "light" ? loaded.Theme : defaults.Theme;
            defaults.IntervalSeconds = PickAllowed(loaded.IntervalSeconds, [1, 2, 5], defaults.IntervalSeconds);
            defaults.LastProfileIndex = Math.Max(0, Math.Min(ProfileCount - 1, loaded.LastProfileIndex));
            defaults.EditFan = loaded.EditFan == 2 ? 2 : 1;
            defaults.SyncFanSpeeds = loaded.SyncFanSpeeds;
            defaults.ControlStrategy = Enum.IsDefined(loaded.ControlStrategy) ? loaded.ControlStrategy : ControlStrategy.FixedRpm;
            defaults.FanCurveWarningAccepted = loaded.FanCurveWarningAccepted;
            defaults.GameExitHoldSeconds = PickAllowed(loaded.GameExitHoldSeconds, [0, 10, 20, 30, 60], defaults.GameExitHoldSeconds);
            defaults.FixedRpm = NormalizeFixedRpmSettings(loaded.FixedRpm ?? defaults.FixedRpm, FallbackMinRpm, FallbackMaxRpm);
            defaults.ResumeFanControlOnNextStart = loaded.ResumeFanControlOnNextStart || loaded.FanControlWasRunning;
            defaults.StartWithWindows = loaded.StartWithWindows;
            defaults.StartToTray = loaded.StartToTray;
            defaults.MinimizeToTray = loaded.MinimizeToTray;
            defaults.CloseToTray = loaded.CloseToTray;
            return defaults;
        }
        catch
        {
            return defaults;
        }
    }

    public static void SaveSettings(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(SettingsPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
    }

    public static int SnapRpm(double value) => (int)Math.Round(value / 100.0) * 100;

    public static int ClampRpm(double value, int minimum, int maximum)
    {
        return Math.Max(minimum, Math.Min(maximum, SnapRpm(value)));
    }

    public static int ClampFixedRpm(double value, int minimum, int maximum)
    {
        if (Math.Abs(value) < 0.1)
            return 0;
        return ClampRpm(value, minimum, maximum);
    }

    public static FixedRpmSettings NormalizeFixedRpmSettings(FixedRpmSettings settings, int minimum, int maximum)
    {
        var result = new FixedRpmSettings
        {
            PowerSavingNormalRpm = ClampFixedRpm(settings.PowerSavingNormalRpm, minimum, maximum),
            PowerSavingGameRpm = ClampFixedRpm(settings.PowerSavingGameRpm, minimum, maximum),
            IntelligentNormalRpm = ClampFixedRpm(settings.IntelligentNormalRpm, minimum, maximum),
            IntelligentGameRpm = ClampFixedRpm(settings.IntelligentGameRpm, minimum, maximum),
            PerformanceNormalRpm = ClampFixedRpm(settings.PerformanceNormalRpm, minimum, maximum),
            PerformanceGameRpm = ClampFixedRpm(settings.PerformanceGameRpm, minimum, maximum),
            GeekNormalRpm = ClampFixedRpm(settings.GeekNormalRpm, minimum, maximum),
            GeekGameRpm = ClampFixedRpm(settings.GeekGameRpm, minimum, maximum),
        };

        result.PowerSavingGameRpm = EnsureGameAtLeastNormal(result.PowerSavingNormalRpm, result.PowerSavingGameRpm, minimum, maximum);
        result.IntelligentGameRpm = EnsureGameAtLeastNormal(result.IntelligentNormalRpm, result.IntelligentGameRpm, minimum, maximum);
        result.PerformanceGameRpm = EnsureGameAtLeastNormal(result.PerformanceNormalRpm, result.PerformanceGameRpm, minimum, maximum);
        result.GeekGameRpm = EnsureGameAtLeastNormal(result.GeekNormalRpm, result.GeekGameRpm, minimum, maximum);
        return result;
    }

    public static List<int> ClampCurve(IEnumerable<int> values, int minimum, int maximum)
    {
        return EnforceNonDecreasing(values.Select(value => ClampRpm(value, minimum, maximum)).ToList());
    }

    public static List<int> EnforceNonDecreasing(IReadOnlyList<int> values)
    {
        var result = values.Select(value => SnapRpm(value)).ToList();
        for (var i = 1; i < result.Count; i++)
        {
            if (result[i] < result[i - 1])
                result[i] = result[i - 1];
        }
        return result;
    }

    public static int Interpolate(int[] temps, IReadOnlyList<int> curve, double? tempC)
    {
        if (tempC is null)
            return 0;
        if (tempC <= temps[0])
            return curve[0];
        if (tempC >= temps[^1])
            return curve[^1];

        for (var i = 0; i < temps.Length - 1; i++)
        {
            if (temps[i] <= tempC && tempC <= temps[i + 1])
            {
                var ratio = (tempC.Value - temps[i]) / (temps[i + 1] - temps[i]);
                return SnapRpm(curve[i] + (curve[i + 1] - curve[i]) * ratio);
            }
        }

        return curve[^1];
    }

    private static List<FanProfile> Defaults()
    {
        var cpuBase = CurveFromAnchors(CpuTemps, [(30, FallbackMinRpm), (45, 1800), (60, 2600), (75, 3800), (90, 5000), (100, FallbackMaxRpm)]);
        var gpuBase = CurveFromAnchors(GpuTemps, [(30, FallbackMinRpm), (45, 1800), (60, 2700), (75, 4200), (90, FallbackMaxRpm)]);
        var profiles = new List<FanProfile>();

        for (var i = 0; i < ProfileCount; i++)
        {
            profiles.Add(new FanProfile
            {
                Name = $"Profile {i + 1}",
                CpuFan1Curve = [.. cpuBase],
                CpuFan2Curve = [.. cpuBase],
                GpuFan1Curve = [.. gpuBase],
                GpuFan2Curve = [.. gpuBase],
                CpuCurve = [.. cpuBase],
                GpuCurve = [.. gpuBase]
            });
        }

        SetBothCpuCurves(profiles[1], cpuBase.Select(value => Math.Max(FallbackMinRpm, value - 300)).ToList());
        SetBothGpuCurves(profiles[1], gpuBase.Select(value => Math.Max(FallbackMinRpm, value - 300)).ToList());
        SetBothCpuCurves(profiles[2], cpuBase.Select(value => Math.Min(FallbackMaxRpm, value + 500)).ToList());
        SetBothGpuCurves(profiles[2], gpuBase.Select(value => Math.Min(FallbackMaxRpm, value + 500)).ToList());
        SetBothGpuCurves(profiles[3], gpuBase.Select(value => Math.Min(FallbackMaxRpm, value + 700)).ToList());
        return profiles;
    }

    private static void SetBothCpuCurves(FanProfile profile, List<int> curve)
    {
        profile.CpuFan1Curve = [.. curve];
        profile.CpuFan2Curve = [.. curve];
        profile.CpuCurve = [.. curve];
    }

    private static void SetBothGpuCurves(FanProfile profile, List<int> curve)
    {
        profile.GpuFan1Curve = [.. curve];
        profile.GpuFan2Curve = [.. curve];
        profile.GpuCurve = [.. curve];
    }

    private static List<int> NormalizeProfileCurve(IReadOnlyList<int>? values, IReadOnlyList<int>? legacyValues, int expectedLength, IReadOnlyList<int> fallback)
    {
        var source = values is { Count: > 0 } ? values : legacyValues;
        return EnforceNonDecreasing(NormalizeCurve(source, expectedLength, fallback));
    }

    private static List<int> NormalizeCurve(IReadOnlyList<int>? values, int expectedLength, IReadOnlyList<int> fallback)
    {
        if (values is null || values.Count != expectedLength)
            return [.. fallback];
        return values.Select(value => SnapRpm(value)).ToList();
    }

    private static double PickAllowed(double value, IReadOnlyList<double> allowed, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            return fallback;

        return allowed
            .OrderBy(candidate => Math.Abs(candidate - value))
            .FirstOrDefault();
    }

    private static int EnsureGameAtLeastNormal(int normal, int game, int minimum, int maximum)
    {
        if (game == 0 || normal == 0 || game >= normal)
            return game;
        return ClampFixedRpm(normal, minimum, maximum);
    }

    private static double NormalizeSmoothingSamples(double value, double fallback)
    {
        if (double.IsNaN(value) || double.IsInfinity(value) || value <= 0)
            return fallback;

        if (value < 1)
        {
            var oldAlpha = 1 - Math.Max(0, Math.Min(0.95, value));
            value = (2.0 / oldAlpha) - 1.0;
        }

        return PickAllowed(value, [1, 2, 3, 5, 10], fallback);
    }

    private static List<int> CurveFromAnchors(int[] temps, IReadOnlyList<(int Temp, int Rpm)> anchors)
    {
        return temps.Select(temp => Interpolate(anchors.Select(item => item.Temp).ToArray(), anchors.Select(item => item.Rpm).ToArray(), temp)).ToList();
    }
}
