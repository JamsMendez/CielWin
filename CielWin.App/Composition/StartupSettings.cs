namespace CielWin.App.Composition;

/// <summary>The production settings read at startup.</summary>
public static class StartupSettings
{
    /// <summary>
    /// Reads <paramref name="path"/>. A missing file (first run, which is the install moment: there is
    /// no installer) gets the commented defaults written so there is a real file to hand-edit. An
    /// existing file is only read, never rewritten. An unreadable one is traced by exception type and
    /// comes back with <see cref="SettingsLoadResult.CanSave"/> false, so nothing later overwrites it.
    /// </summary>
    public static SettingsLoadResult Load(string path, Action<string> trace)
    {
        var result = SettingsFile.TryLoad(path, type => trace($"settings-file read-failed error={type}"));
        if (result.Status == SettingsLoadStatus.Missing)
        {
            SettingsFile.Save(path, Settings.Default, type => trace($"settings-file save-failed error={type}"));
        }

        return result;
    }
}
