using System.Diagnostics;

namespace SCSCompanion.Services;

public sealed class MixxxProfileInstallerService
{
    private const string ProfileFileName = "SCS Companion DJ Utilities.midi.xml";
    private readonly string profileSourcePath = Path.Combine(AppContext.BaseDirectory, "Profiles", "Mixxx", ProfileFileName);
    private readonly string profileDestinationPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Mixxx", "controllers", ProfileFileName);

    public bool IsMixxxInstalled => FindMixxxExecutable() is not null;

    public bool CanInstall => IsMixxxInstalled && File.Exists(profileSourcePath);

    public string Status
    {
        get
        {
            var executable = FindMixxxExecutable();
            if (executable is null)
            {
                return "Mixxx not detected · manual install available";
            }

            var version = FileVersionInfo.GetVersionInfo(executable).ProductVersion?.Replace(" x64", string.Empty) ?? "detected";
            if (!File.Exists(profileSourcePath))
            {
                return $"Mixxx {version} · bundled profile missing";
            }
            if (!File.Exists(profileDestinationPath))
            {
                return $"Mixxx {version} · profile not installed";
            }

            return FilesMatch(profileSourcePath, profileDestinationPath)
                ? $"Mixxx {version} · profile installed"
                : $"Mixxx {version} · profile update available";
        }
    }

    public ProfileInstallResult InstallOrRepair()
    {
        if (!IsMixxxInstalled)
        {
            return new(false, "Mixxx was not found in its standard installation folders.");
        }
        if (!File.Exists(profileSourcePath))
        {
            return new(false, "The bundled Mixxx profile is missing from this companion build.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(profileDestinationPath)!);
            File.Copy(profileSourcePath, profileDestinationPath, overwrite: true);
            return new(true, "Profile installed · reopen Mixxx Preferences to load it");
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "Windows blocked the profile copy. Use the manual path in the documentation.");
        }
        catch (IOException exception)
        {
            return new(false, $"Profile copy failed · {exception.Message}");
        }
    }

    private static string? FindMixxxExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Mixxx", "mixxx.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Mixxx", "mixxx.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool FilesMatch(string first, string second)
    {
        var firstInfo = new FileInfo(first);
        var secondInfo = new FileInfo(second);
        return firstInfo.Length == secondInfo.Length && File.ReadAllBytes(first).AsSpan().SequenceEqual(File.ReadAllBytes(second));
    }
}

public sealed record ProfileInstallResult(bool Success, string Message);
