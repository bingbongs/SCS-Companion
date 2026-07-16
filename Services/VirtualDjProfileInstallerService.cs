using System.Diagnostics;

namespace SCSCompanion.Services;

public sealed class VirtualDjProfileInstallerService
{
    private const string ProfileFileName = "SCS Companion DJ Utilities.xml";
    private readonly string definitionSourcePath = Path.Combine(AppContext.BaseDirectory, "Profiles", "VirtualDJ", "Devices", ProfileFileName);
    private readonly string mapperSourcePath = Path.Combine(AppContext.BaseDirectory, "Profiles", "VirtualDJ", "Mappers", ProfileFileName);
    private readonly string definitionDestinationPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VirtualDJ", "Devices", ProfileFileName);
    private readonly string mapperDestinationPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "VirtualDJ", "Mappers", ProfileFileName);

    public bool IsVirtualDjInstalled => FindVirtualDjExecutable() is not null;

    public bool CanInstall => IsVirtualDjInstalled && File.Exists(definitionSourcePath) && File.Exists(mapperSourcePath);

    public string Status
    {
        get
        {
            var executable = FindVirtualDjExecutable();
            if (executable is null)
            {
                return "VirtualDJ not detected · manual install available";
            }

            var version = FileVersionInfo.GetVersionInfo(executable).ProductVersion ?? "detected";
            if (!File.Exists(definitionSourcePath) || !File.Exists(mapperSourcePath))
            {
                return $"VirtualDJ {version} · bundled profile missing";
            }
            if (!File.Exists(definitionDestinationPath) || !File.Exists(mapperDestinationPath))
            {
                return $"VirtualDJ {version} · profile not installed";
            }

            return FilesMatch(definitionSourcePath, definitionDestinationPath) && FilesMatch(mapperSourcePath, mapperDestinationPath)
                ? $"VirtualDJ {version} · profile installed"
                : $"VirtualDJ {version} · profile update available";
        }
    }

    public ProfileInstallResult InstallOrRepair()
    {
        if (!IsVirtualDjInstalled)
        {
            return new(false, "VirtualDJ was not found in its standard installation folders.");
        }
        if (!File.Exists(definitionSourcePath) || !File.Exists(mapperSourcePath))
        {
            return new(false, "The bundled VirtualDJ profile is missing from this companion build.");
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(definitionDestinationPath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(mapperDestinationPath)!);
            File.Copy(definitionSourcePath, definitionDestinationPath, overwrite: true);
            File.Copy(mapperSourcePath, mapperDestinationPath, overwrite: true);
            return new(true, "Profile installed · restart VirtualDJ to load it");
        }
        catch (UnauthorizedAccessException)
        {
            return new(false, "Windows blocked the profile copy. Use the manual paths in the documentation.");
        }
        catch (IOException exception)
        {
            return new(false, $"Profile copy failed · {exception.Message}");
        }
    }

    private static string? FindVirtualDjExecutable()
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "VirtualDJ", "virtualdj.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "VirtualDJ", "virtualdj.exe"),
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
