using EnvDTE;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.Permissions;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;
using Process = System.Diagnostics.Process;

namespace KiloVisualStudioExtension.Utils
{
  public static class EditorActions
  {
    private const int MaximumFallbackMatches = 5;

    public sealed class Options
    {
      public Func<string> Dir { get; set; } = () => string.Empty;

      // Reserved for the future DiffVirtualProvider implementation.
      public IntegratedDiffProvider? Diff { get; set; }

      // Can contain a filesystem path.
      public object? Storage { get; set; }

      public Action<object?>? Post { get; set; }
    }

    public static async Task<bool> HandleEditorActionAsync(
        IEditorActionMessage message,
        Options options)
    {
      if (message == null)
        return false;

      if (message is OpenFileRequest openFileRequest)
      {
        if (!string.IsNullOrWhiteSpace(openFileRequest.FilePath))
        {
          await OpenFileAsync(
              options.Dir(),
              openFileRequest.FilePath,
              (int?)openFileRequest.Line,
              (int?)openFileRequest.Column);
        }

        return true;
      }

      if (message is OpenContentRequest openContentRequest)
      {
        if (!string.IsNullOrEmpty(openContentRequest.Content))
        {
          await OpenContentAsync(
              openContentRequest.Content,
              openContentRequest.Language);
        }

        return true;
      }

      if (message is ValidateFilesRequest validateFilesRequest)
      {
        if (!string.IsNullOrWhiteSpace(validateFilesRequest.Id) &&
            validateFilesRequest.Paths != null &&
            options.Post != null)
        {
          var existing = await ValidateFilesAsync(
              options.Dir(),
              validateFilesRequest.Paths.ToArray());

          options.Post(new
          {
            type = "validateFilesResult",
            id = validateFilesRequest.Id,
            existing
          });
        }

        return true;
      }

      if (message is OpenExternalRequest openExternalRequest)
      {
        OpenExternal(openExternalRequest.Url);
        return true;
      }

      if (message is OpenDiffVirtualRequest openDiffVirtualRequest)
      {
        await OpenIntegratedDiffAsync(openDiffVirtualRequest);
        return true;
      }

      if (message is PreviewImageRequest previewImageRequest)
      {
        if (!string.IsNullOrWhiteSpace(
                previewImageRequest.DataUrl) &&
            !string.IsNullOrWhiteSpace(
                previewImageRequest.Filename))
        {
          await PreviewImageAsync(
              options.Storage,
              previewImageRequest.DataUrl,
              previewImageRequest.Filename);
        }

        return true;
      }

      return false;
    }

    private static IntegratedDiffProvider? _diffProvider;

    private static async Task OpenIntegratedDiffAsync(
        OpenDiffVirtualRequest request)
    {
      try
      {
        if (request.Diff == null)
          return;

        var diff = ConvertDiff(request.Diff);

        if (diff == null)
          return;

        /*
         * This assumes that your request or DTO provides the proposed
         * content. If it only provides a unified patch, apply the patch
         * first to obtain modifiedContent.
         */
        var originalPath = diff.File;

        if (!File.Exists(originalPath))
        {
          Debug.WriteLine(
              $"[Kilo] Cannot open diff. File not found: {originalPath}");

          return;
        }

        var originalContent =
            await AsyncUtils.ReadAllTextAsync(originalPath);

        var proposedContent =
            UnifiedPatchService.Apply(
                originalContent,
                diff.Patch ?? string.Empty);

        _diffProvider ??= new IntegratedDiffProvider();

        await _diffProvider.OpenAsync(
            diff,
            originalContent,
            proposedContent);
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] Failed to open integrated diff: {ex}");
        System.Diagnostics.Debug.WriteLine(
        $"[Kilo] Failed to apply diff patch: {ex}");

        System.Windows.MessageBox.Show(
            $"Unable to apply the proposed changes:\n\n{ex.Message}",
            "Kilo Diff",
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Error);
      }
    }

    private static DiffVirtualFile ConvertDiff(PermissionFileDiff diff)
    {
      return new DiffVirtualFile
      {
        Additions = (int)diff.Additions,
        Deletions = (int)diff.Deletions,
        File = diff.File,
        InitialDiffStyle = "unified",
        Patch = diff.Patch
      };
    }

    private static async Task<EnvDTE.DTE?> GetDteAsync()
    {
      await ThreadHelper.JoinableTaskFactory
          .SwitchToMainThreadAsync();

      return await KiloProvider.Package
          .GetServiceAsync(typeof(EnvDTE.DTE)) as EnvDTE.DTE;
    }

    private static void OpenExternal(object? value)
    {
      if (value is not string url ||
          string.IsNullOrWhiteSpace(url))
      {
        return;
      }

      try
      {
        Process.Start(new ProcessStartInfo
        {
          FileName = url,
          UseShellExecute = true
        });
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] Failed to open external URL: {ex}");
      }
    }

    private static async Task OpenContentAsync(
        string content,
        string? language)
    {
      try
      {
        var extension = GetLanguageExtension(language);

        var temporaryFile = Path.Combine(
            Path.GetTempPath(),
            $"kilo-content-{Guid.NewGuid():N}{extension}");

        await AsyncUtils.WriteAllTextAsync(
            temporaryFile,
            content);

        var dte = await GetDteAsync();

        dte?.ItemOperations.OpenFile(temporaryFile);
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] Failed to open content: {ex}");
      }
    }

    private static string GetLanguageExtension(
        string? language)
    {
      return language?.Trim().ToLowerInvariant() switch
      {
        "javascript" or "js" => ".js",
        "typescript" or "ts" => ".ts",
        "javascriptreact" or "jsx" => ".jsx",
        "typescriptreact" or "tsx" => ".tsx",
        "csharp" or "cs" => ".cs",
        "json" => ".json",
        "markdown" or "md" => ".md",
        "html" => ".html",
        "css" => ".css",
        "log" => ".log",
        _ => ".log"
      };
    }

    private static async Task OpenFileAsync(
        string dir,
        string filePath,
        int? line,
        int? column)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(filePath))
          return;

        var fullPath = MakeFullPath(dir, filePath);

        if (Directory.Exists(fullPath))
        {
          OpenDirectoryInExplorer(fullPath);
          return;
        }

        if (File.Exists(fullPath))
        {
          await ShowDocumentAsync(
              fullPath,
              line,
              column);

          return;
        }

        await FindFallbackAsync(
            dir,
            filePath,
            line,
            column);
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] Failed to open file '{filePath}': {ex}");
      }
    }

    private static string MakeFullPath(
        string dir,
        string path)
    {
      if (Path.IsPathRooted(path))
        return Path.GetFullPath(path);

      return Path.GetFullPath(
          Path.Combine(dir, path));
    }

    private static void OpenDirectoryInExplorer(
        string directory)
    {
      try
      {
        Process.Start(new ProcessStartInfo
        {
          FileName = "explorer.exe",
          Arguments = $"\"{directory}\"",
          UseShellExecute = true
        });
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] Failed to open directory '{directory}': {ex}");
      }
    }

    private static async Task ShowDocumentAsync(
        string filePath,
        int? line,
        int? column)
    {
      try
      {
        var dte = await GetDteAsync();

        if (dte == null)
          return;

        var window = dte.ItemOperations.OpenFile(filePath);

        if (window?.Document == null)
          return;

        // This matches the original VS Code behavior:
        // open the document without changing the cursor if no line
        // was provided.
        if (!line.HasValue || line.Value <= 0)
          return;

        var selection =
            window.Document.Selection as TextSelection;

        if (selection == null)
          return;

        var textDocument =
            window.Document.Object("TextDocument")
            as TextDocument;

        if (textDocument == null)
          return;

        var targetLine = Math.Max(1, line.Value);
        var lastLine = Math.Max(
            1,
            textDocument.EndPoint.Line);

        if (targetLine > lastLine)
          targetLine = lastLine;

        /*
         * The incoming protocol uses one-based line and column
         * values. EnvDTE also uses one-based values.
         */
        var targetColumn =
            column.HasValue && column.Value > 0
                ? column.Value
                : 1;

        selection.MoveToLineAndOffset(
            targetLine,
            targetColumn,
            false);
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] Failed to show document '{filePath}': {ex}");
      }
    }

    private static async Task FindFallbackAsync(
        string dir,
        string filePath,
        int? line,
        int? column)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(dir) ||
            !Directory.Exists(dir))
        {
          await ShowFileNotFoundAsync(filePath);
          return;
        }

        var fileName = Path.GetFileName(filePath);

        if (string.IsNullOrWhiteSpace(fileName))
        {
          await ShowFileNotFoundAsync(filePath);
          return;
        }

        var matches = await Task.Run(() =>
            FindFilesByName(
                dir,
                fileName,
                MaximumFallbackMatches));

        if (matches.Count == 0)
        {
          await ShowFileNotFoundAsync(filePath);
          return;
        }

        if (matches.Count == 1)
        {
          await ShowDocumentAsync(
              matches[0],
              line,
              column);

          return;
        }

        var entries = matches
            .Select(path => new QuickPickEntry
            {
              Label = GetRelativePathSafe(dir, path),
              Description = path,
              Item = path
            })
            .ToList();

        var selected =
            await QuickPickHelper.ShowQuickPickAsync(
                entries,
                new QuickPickOptions
                {
                  Title =
                        $"Multiple matches for \"{fileName}\"",
                  PlaceHolder =
                        "Select a file to open",
                  MatchOnDescription = true,
                  MatchOnDetail = false
                });

        if (selected?.Item is string selectedPath)
        {
          await ShowDocumentAsync(
              selectedPath,
              line,
              column);
        }
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] Fallback search failed: {ex}");
      }
    }

    private static List<string> FindFilesByName(
        string rootDirectory,
        string fileName,
        int maximumResults)
    {
      var results = new List<string>();

      try
      {
        foreach (var path in Directory.EnumerateFiles(
            rootDirectory,
            fileName,
            SearchOption.AllDirectories))
        {
          if (IsInsideNodeModules(path))
            continue;

          results.Add(path);

          if (results.Count >= maximumResults)
            break;
        }
      }
      catch (UnauthorizedAccessException)
      {
        // Ignore inaccessible directories.
      }
      catch (DirectoryNotFoundException)
      {
        // The directory may have disappeared during enumeration.
      }
      catch (IOException)
      {
        // Ignore filesystem enumeration errors.
      }

      return results;
    }

    private static bool IsInsideNodeModules(
        string path)
    {
      var normalizedPath = path
          .Replace(
              Path.AltDirectorySeparatorChar,
              Path.DirectorySeparatorChar)
          .TrimEnd(Path.DirectorySeparatorChar);

      var parts = normalizedPath.Split(
          Path.DirectorySeparatorChar);

      return parts.Any(part =>
          string.Equals(
              part,
              "node_modules",
              StringComparison.OrdinalIgnoreCase));
    }

    private static string GetRelativePathSafe(
        string rootDirectory,
        string path)
    {
      try
      {
        return PathUtils.GetRelativePath(
            rootDirectory,
            path);
      }
      catch
      {
        return path;
      }
    }

    private static async Task ShowFileNotFoundAsync(
        string filePath)
    {
      await ThreadHelper.JoinableTaskFactory
          .SwitchToMainThreadAsync();

      MessageBox.Show(
          $"File not found: {filePath}",
          "Kilo",
          MessageBoxButton.OK,
          MessageBoxImage.Warning);
    }

    private static async Task<List<string>> ValidateFilesAsync(
        string dir,
        string[] paths)
    {
      var existing = new List<string>();

      foreach (var path in paths)
      {
        if (string.IsNullOrWhiteSpace(path))
          continue;

        var fullPath = MakeFullPath(dir, path);

        if (File.Exists(fullPath))
          existing.Add(path);
      }

      return await Task.FromResult(existing);
    }

    private static async Task PreviewImageAsync(
        object? storage,
        string dataUrl,
        string filename)
    {
      try
      {
        var imageBytes = ParseImage(dataUrl);

        if (imageBytes == null)
          return;

        var storagePath = storage as string;

        var previewDirectory =
            string.IsNullOrWhiteSpace(storagePath)
                ? Path.Combine(
                    Path.GetTempPath(),
                    "kilo-preview")
                : Path.Combine(
                    storagePath,
                    "kilo-preview");

        Directory.CreateDirectory(previewDirectory);

        var baseName =
            Path.GetFileNameWithoutExtension(filename);

        var extension =
            Path.GetExtension(filename);

        if (string.IsNullOrWhiteSpace(extension))
          extension = ".img";

        var previewFile = Path.Combine(
            previewDirectory,
            $"{baseName}_{DateTime.UtcNow.Ticks}{extension}");

        await AsyncUtils.WriteAllBytesAsync(
            previewFile,
            imageBytes);

        await CleanOldPreviewsAsync(
            previewDirectory);

        await ShowDocumentAsync(
            previewFile,
            null,
            null);
      }
      catch (Exception ex)
      {
        Debug.WriteLine(
            $"[Kilo] Failed to preview image: {ex}");
      }
    }

    private static byte[]? ParseImage(
        string dataUrl)
    {
      try
      {
        if (string.IsNullOrWhiteSpace(dataUrl) ||
            !dataUrl.StartsWith(
                "data:image/",
                StringComparison.OrdinalIgnoreCase))
        {
          return null;
        }

        var commaIndex = dataUrl.IndexOf(',');

        if (commaIndex < 0)
          return null;

        var metadata = dataUrl.Substring(
            0,
            commaIndex);

        if (!metadata.Contains(
            ";base64",
            StringComparison.OrdinalIgnoreCase))
        {
          return null;
        }

        var base64 = dataUrl.Substring(
            commaIndex + 1);

        return Convert.FromBase64String(base64);
      }
      catch
      {
        return null;
      }
    }

    private static Task CleanOldPreviewsAsync(
        string previewDirectory)
    {
      try
      {
        if (!Directory.Exists(previewDirectory))
          return Task.CompletedTask;

        var files = Directory
            .EnumerateFiles(previewDirectory)
            .OrderByDescending(
                File.GetLastWriteTimeUtc)
            .Skip(10)
            .ToList();

        foreach (var file in files)
        {
          try
          {
            File.Delete(file);
          }
          catch
          {
            // Ignore individual deletion failures.
          }
        }
      }
      catch
      {
        // Ignore cleanup failures.
      }

      return Task.CompletedTask;
    }
  }
}
