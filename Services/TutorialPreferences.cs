using System;
using System.IO;
using System.Text.Json;

namespace GithubMirror.Services;

public static class TutorialPreferences
{
    public static bool LoadHidden()
    {
        try { return new AppPreferenceStore().Load().HideTutorial; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    public static void SaveHidden(bool hidden)
    {
        var store = new AppPreferenceStore();
        var preferences = store.Load();
        preferences.HideTutorial = hidden;
        store.Save(preferences);
    }
}
