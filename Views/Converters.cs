using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using GithubMirror.Models;

namespace GithubMirror.Views;

/// <summary>平台 → 識別色（自訂色票，非官方商標）。</summary>
public sealed class PlatformBrushConverter : IValueConverter
{
    public static readonly PlatformBrushConverter Instance = new();

    private static readonly Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        [PlatformIds.GitHub] = "#24292F",
        [PlatformIds.GitLab] = "#E24329",
        [PlatformIds.Bitbucket] = "#0052CC",
        [PlatformIds.Codeberg] = "#2185D0",
        [PlatformIds.Gitea] = "#5A9E28",
        [PlatformIds.AwsCodeCommit] = "#E08A00",
        [PlatformIds.AzureRepos] = "#0078D4",
        ["ALL"] = "#475569"
    };

    private static readonly Dictionary<string, IBrush> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? string.Empty;
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var hex = Colors.TryGetValue(key, out var c) ? c : "#6E7781";
        var brush = new SolidColorBrush(Color.Parse(hex));
        Cache[key] = brush;
        return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>平台 → 徽章上的縮寫字。</summary>
public sealed class PlatformInitialsConverter : IValueConverter
{
    public static readonly PlatformInitialsConverter Instance = new();

    private static readonly Dictionary<string, string> Initials = new(StringComparer.OrdinalIgnoreCase)
    {
        [PlatformIds.GitHub] = "GH",
        [PlatformIds.GitLab] = "GL",
        [PlatformIds.Bitbucket] = "BB",
        [PlatformIds.Codeberg] = "CB",
        [PlatformIds.Gitea] = "GT",
        [PlatformIds.AwsCodeCommit] = "AWS",
        [PlatformIds.AzureRepos] = "AZ",
        ["ALL"] = "全"
    };

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string ?? string.Empty;
        if (Initials.TryGetValue(key, out var s)) return s;
        return key.Length > 0 ? key[..1].ToUpperInvariant() : "?";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>任務狀態 → 圖示。</summary>
public sealed class StatusGeometryConverter : IValueConverter
{
    public static readonly StatusGeometryConverter Instance = new();

    private const string Check = "M2,12 A10,10 0 1,1 22,12 A10,10 0 1,1 2,12 Z M10.8,17 L18.2,9.6 L16.8,8.2 L10.8,14.2 L7.9,11.3 L6.5,12.7 Z";
    private const string Alert = "M2,12 A10,10 0 1,1 22,12 A10,10 0 1,1 2,12 Z M11,6.5 H13 V13.5 H11 Z M11,15.4 H13 V17.5 H11 Z";
    private const string Clock = "M2,12 A10,10 0 1,1 22,12 A10,10 0 1,1 2,12 Z M4,12 A8,8 0 1,0 20,12 A8,8 0 1,0 4,12 Z M11.1,6.5 H12.9 V12.2 L17,14.6 L16.1,16.1 L11.1,13.2 Z";
    private const string Running = "M12,5 V1.5 L7.5,6 L12,10.5 V7 A5,5 0 1,1 7,12 H5 A7,7 0 1,0 12,5 Z";
    private const string Stop = "M6.5,6.5 H17.5 V17.5 H6.5 Z";

    private static readonly Dictionary<string, Geometry> Cache = new();

    private static Geometry Get(string data)
    {
        if (Cache.TryGetValue(data, out var g)) return g;
        var parsed = StreamGeometry.Parse(data);
        Cache[data] = parsed;
        return parsed;
    }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is MirrorStatus status
            ? status switch
            {
                MirrorStatus.Completed => Get(Check),
                MirrorStatus.Failed => Get(Alert),
                MirrorStatus.Running => Get(Running),
                MirrorStatus.Cancelled => Get(Stop),
                _ => Get(Clock)
            }
            : Get(Clock);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>任務狀態 → 顏色。</summary>
public sealed class StatusBrushConverter : IValueConverter
{
    public static readonly StatusBrushConverter Instance = new();

    private static readonly IBrush Success = new SolidColorBrush(Color.Parse("#1A7F37"));
    private static readonly IBrush Danger = new SolidColorBrush(Color.Parse("#CF222E"));
    private static readonly IBrush Accent = new SolidColorBrush(Color.Parse("#2563EB"));
    private static readonly IBrush Muted = new SolidColorBrush(Color.Parse("#6E7781"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is MirrorStatus status
            ? status switch
            {
                MirrorStatus.Completed => Success,
                MirrorStatus.Failed => Danger,
                MirrorStatus.Running => Accent,
                _ => Muted
            }
            : Muted;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>IsPrivate → 鎖頭 / 地球圖示。</summary>
public sealed class PrivacyGeometryConverter : IValueConverter
{
    public static readonly PrivacyGeometryConverter Instance = new();

    private const string Lock = "M12,2 A5,5 0 0,1 17,7 V9.5 H19 V22 H5 V9.5 H7 V7 A5,5 0 0,1 12,2 Z M12,4 A3,3 0 0,0 9,7 V9.5 H15 V7 A3,3 0 0,0 12,4 Z";
    private const string Globe = "M2,12 A10,10 0 1,1 22,12 A10,10 0 1,1 2,12 Z M4,12 A8,8 0 1,0 20,12 A8,8 0 1,0 4,12 Z M11,2.4 H13 V21.6 H11 Z M2.6,11 H21.4 V13 H2.6 Z M12,2.6 A9,14 0 0,1 12,21.4 A9,14 0 0,1 12,2.6 Z M12,4.6 A7,12 0 0,0 12,19.4 A7,12 0 0,0 12,4.6 Z";

    private static readonly Geometry LockGeometry = StreamGeometry.Parse(Lock);
    private static readonly Geometry GlobeGeometry = StreamGeometry.Parse(Globe);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? LockGeometry : GlobeGeometry;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class PrivacyBrushConverter : IValueConverter
{
    public static readonly PrivacyBrushConverter Instance = new();

    private static readonly IBrush Private = new SolidColorBrush(Color.Parse("#BF8700"));
    private static readonly IBrush Public = new SolidColorBrush(Color.Parse("#6E7781"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Private : Public;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public static readonly InverseBoolConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not true;
}
