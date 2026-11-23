namespace SpectraPlayer.Core;

public static class AppPaths
{
    /// <summary>
    /// Where the library database lives: %LOCALAPPDATA%\Spectraplayer, or the folder named by the
    /// SPECTRAPLAYER_DATA environment variable (used for tests and portable setups).
    /// </summary>
    public static string DataDirectory
    {
        get
        {
            var overridden = Environment.GetEnvironmentVariable("SPECTRAPLAYER_DATA");
            if (!string.IsNullOrWhiteSpace(overridden)) return overridden;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Spectraplayer");
        }
    }

    public static string LibraryDatabasePath => Path.Combine(DataDirectory, "library.db");
}
