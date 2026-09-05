using System;
using System.IO;
using System.Text.Json;

namespace GithubMirror.Services;

public sealed class AppPreferences
{
    public bool HideTutorial { get; set; }
    public string NameTemplate { get; set; } = "{name}";
    public bool KeepPrivate { get; set; } = true;
    public bool AutoPlayMusic { get; set; } = true;
    public bool ContinuePlayback { get; set; } = true;
    public string MusicSourceUrl { get; set; } = string.Empty;
    public bool IncludeCollaboratorRepos { get; set; }
    public bool IncludeAllAccessibleRepos { get; set; }
    public bool ShowAllCommitSizes { get; set; }
}

/// <summary>Only non-secret preferences belong in this file.</summary>
public sealed class AppPreferenceStore
{
    private readonly string _directory;
    private string FilePath => Path.Combine(_directory, "preferences.json");
    public AppPreferenceStore(string? directory = null) => _directory = directory ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GithubMirror");

    public AppPreferences Load()
    {
        if (File.Exists(FilePath))
        {
            var value = JsonSerializer.Deserialize<AppPreferences>(File.ReadAllText(FilePath));
            if (value is null || value.NameTemplate is null || value.MusicSourceUrl is null)
                throw new InvalidDataException("本機設定內容不完整。");
            return value;
        }
        var legacy = Path.Combine(_directory, "tutorial-preferences.json");
        return new AppPreferences
        {
            HideTutorial = File.Exists(legacy) && JsonSerializer.Deserialize<bool>(File.ReadAllText(legacy))
        };
    }

    public void Save(AppPreferences preferences)
    {
        Directory.CreateDirectory(_directory);
        var temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(preferences));
            File.Move(temporary, FilePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
