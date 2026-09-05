using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GithubMirror.Models;

public class RepositoryInfo : ObservableObject
{
    private bool _isSelected;

    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;

    /// <summary>平台原生 ID（GitLab 專案 ID、Azure repo GUID 等），建立目標時可能用得到。</summary>
    public string NativeId { get; set; } = string.Empty;

    public string CloneUrl { get; set; } = string.Empty;
    public string WebUrl { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DefaultBranch { get; set; } = string.Empty;

    /// <summary>repository 大小（bytes）。null 代表該平台沒有提供。</summary>
    public long? SizeInBytes { get; set; }

    public int Stars { get; set; }
    public int Forks { get; set; }
    public bool IsPrivate { get; set; }
    public DateTime? LastUpdated { get; set; }
    public string Language { get; set; } = string.Empty;
    public List<string> Topics { get; set; } = new();

    public string SourcePlatform { get; set; } = string.Empty;
    public string AccountId { get; set; } = string.Empty;
    public string AccountDisplay { get; set; } = string.Empty;

    /// <summary>清單勾選狀態，用於批次鏡像。</summary>
    [JsonIgnore]
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string FullName => string.IsNullOrEmpty(Owner) ? Name : $"{Owner}/{Name}";

    public string SizeDisplay => FormatSize(SizeInBytes);

    public string VisibilityDisplay => IsPrivate ? "Private" : "Public";

    public string LastUpdatedDisplay =>
        LastUpdated.HasValue ? LastUpdated.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "—";

    public string LanguageDisplay => string.IsNullOrWhiteSpace(Language) ? "—" : Language;

    public string DescriptionDisplay => string.IsNullOrWhiteSpace(Description) ? "—" : Description;

    public static string FormatSize(long? bytes)
    {
        if (!bytes.HasValue) return "—";
        if (bytes.Value <= 0) return "0 KB";

        double kb = bytes.Value / 1024.0;
        if (kb < 1024) return $"{kb:N0} KB";

        double mb = kb / 1024.0;
        if (mb < 1024) return $"{mb:N1} MB";

        double gb = mb / 1024.0;
        return $"{gb:N2} GB";
    }
}
