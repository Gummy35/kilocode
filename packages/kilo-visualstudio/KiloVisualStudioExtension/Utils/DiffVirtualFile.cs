using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace KiloVisualStudioExtension.Utils
{
  public sealed class DiffVirtualFile
  {
    public string File { get; set; } = string.Empty;

    public string? Patch { get; set; }

    public int Additions { get; set; }

    public int Deletions { get; set; }

    public string InitialDiffStyle { get; set; } = "unified";
  }

  /// <summary>
  /// Opens in-memory file changes using Visual Studio's integrated
  /// difference viewer.
  /// </summary>
  public sealed class IntegratedDiffProvider : IDisposable
  {
    private readonly List<string> _temporaryFiles = new();

    private bool _disposed;

    /// <summary>
    /// Opens a diff where the left and right contents are already available.
    /// </summary>
    public async Task OpenAsync(
        DiffVirtualFile diff,
        string originalContent,
        string modifiedContent)
    {
      if (diff == null)
        throw new ArgumentNullException(nameof(diff));

      if (string.IsNullOrWhiteSpace(diff.File))
        throw new ArgumentException(
            "The diff file path is required.",
            nameof(diff));

      if (_disposed)
        throw new ObjectDisposedException(
            nameof(IntegratedDiffProvider));

      var fileName = Path.GetFileName(diff.File);

      if (string.IsNullOrWhiteSpace(fileName))
        fileName = "file";

      var safeName = MakeSafeFileName(fileName);
      var id = Guid.NewGuid().ToString("N");

      var leftPath = Path.Combine(
          Path.GetTempPath(),
          $"kilo-diff-{id}-original-{safeName}");

      var rightPath = Path.Combine(
          Path.GetTempPath(),
          $"kilo-diff-{id}-modified-{safeName}");

      await AsyncUtils.WriteAllTextAsync(
          leftPath,
          originalContent ?? string.Empty);

      await AsyncUtils.WriteAllTextAsync(
          rightPath,
          modifiedContent ?? string.Empty);

      _temporaryFiles.Add(leftPath);
      _temporaryFiles.Add(rightPath);

      await OpenComparisonAsync(
          leftPath,
          rightPath,
          fileName,
          diff);
    }

    /// <summary>
    /// Opens a diff using the original file as the right side.
    /// This is useful when only the proposed content is temporary.
    /// </summary>
    public async Task OpenAgainstFileAsync(
        DiffVirtualFile diff,
        string originalFilePath,
        string modifiedContent)
    {
      if (diff == null)
        throw new ArgumentNullException(nameof(diff));

      if (string.IsNullOrWhiteSpace(originalFilePath))
        throw new ArgumentException(
            "The original file path is required.",
            nameof(originalFilePath));

      if (!File.Exists(originalFilePath))
        throw new FileNotFoundException(
            "The original file was not found.",
            originalFilePath);

      var fileName = Path.GetFileName(originalFilePath);

      if (string.IsNullOrWhiteSpace(fileName))
        fileName = "file";

      var safeName = MakeSafeFileName(fileName);
      var id = Guid.NewGuid().ToString("N");

      var modifiedPath = Path.Combine(
          Path.GetTempPath(),
          $"kilo-diff-{id}-modified-{safeName}");

      await AsyncUtils.WriteAllTextAsync(
          modifiedPath,
          modifiedContent ?? string.Empty);

      _temporaryFiles.Add(modifiedPath);

      await OpenComparisonAsync(
          originalFilePath,
          modifiedPath,
          fileName,
          diff);
    }

    private static async Task OpenComparisonAsync(
        string leftPath,
        string rightPath,
        string fileName,
        DiffVirtualFile diff)
    {
      await ThreadHelper.JoinableTaskFactory
          .SwitchToMainThreadAsync();

      var differenceService =
          await KiloProvider.Package.GetServiceAsync(
              typeof(SVsDifferenceService))
          as IVsDifferenceService;

      if (differenceService == null)
      {
        Debug.WriteLine(
            "[Kilo] Visual Studio difference service is unavailable.");

        return;
      }

      var initialStyle =
          string.Equals(
              diff.InitialDiffStyle,
              "split",
              StringComparison.OrdinalIgnoreCase)
              ? "split"
              : "unified";

      var title =
          $"Changes: {fileName}";

      var tooltip =
          $"Kilo changes for {fileName}";

      var leftLabel =
          $"Original ({initialStyle})";

      var rightLabel =
          "Proposed";

      differenceService.OpenComparisonWindow2(
          leftPath,
          rightPath,
          title,
          tooltip,
          leftLabel,
          rightLabel,
          null,
          null,
          0);
    }

    private static string MakeSafeFileName(
        string fileName)
    {
      foreach (var invalidCharacter in
               Path.GetInvalidFileNameChars())
      {
        fileName = fileName.Replace(
            invalidCharacter,
            '_');
      }

      return fileName;
    }

    public void Dispose()
    {
      if (_disposed)
        return;

      _disposed = true;

      foreach (var path in _temporaryFiles)
      {
        try
        {
          if (File.Exists(path))
            File.Delete(path);
        }
        catch (Exception ex)
        {
          Debug.WriteLine(
              $"[Kilo] Failed to delete temporary diff file " +
              $"{path}: {ex.Message}");
        }
      }

      _temporaryFiles.Clear();
    }
  }
}
