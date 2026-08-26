using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace KiloVisualStudioExtension.Utils
{

    /// <summary>
    /// Detects indexing plugin presence in plugin specifications.
    /// Matches the TypeScript implementation in kilo-indexing/src/detect.ts.
    /// </summary>
    public static class IndexingPluginDetector
    {
      private static readonly HashSet<string> IndexingPluginNames = new HashSet<string>
        {
            "kilo-indexing",
            "@kilocode/kilo-indexing"
        };

      private static readonly Regex PathRegex = new Regex(@"^[A-Za-z]:[\\/]", RegexOptions.Compiled);
      private static readonly Regex FileExtensionRegex = new Regex(@"\.[cm]?[jt]s$", RegexOptions.Compiled);

      /// <summary>
      /// Checks if a plugin specifier is an indexing plugin.
      /// </summary>
      /// <param name="value">The plugin specifier (string or tuple of [name, ...options]).</param>
      /// <returns>True if the specifier refers to an indexing plugin.</returns>
      public static bool IsIndexingPlugin(object value)
      {
        var name = value switch
        {
          string str => str,
          IList<object> list when list.Count > 0 => list[0]?.ToString() ?? "",
          _ => value?.ToString() ?? ""
        };

        return IndexingPluginNames.Contains(NormalizePluginName(name));
      }

      /// <summary>
      /// Checks if any of the provided plugin specifiers is an indexing plugin.
      /// </summary>
      /// <param name="values">The list of plugin specifiers.</param>
      /// <returns>True if at least one specifier refers to an indexing plugin.</returns>
      public static bool HasIndexingPlugin(IEnumerable<object>? values)
      {
        if (values == null)
          return false;

        foreach (var value in values)
        {
          if (IsIndexingPlugin(value))
            return true;
        }

        return false;
      }

      /// <summary>
      /// Normalizes a plugin name to a canonical form.
      /// </summary>
      /// <param name="value">The plugin specifier to normalize.</param>
      /// <returns>The normalized plugin name.</returns>
      public static string NormalizePluginName(string value)
      {
        if (string.IsNullOrEmpty(value))
          return "";

        if (value.StartsWith("file://"))
        {
          return NormalizePath(FromFileUrl(value));
        }

        if (IsPathSpecifier(value))
        {
          return NormalizePath(value);
        }

        return NormalizePackage(value);
      }

      private static string NormalizePackage(string value)
      {
        return StripVersion(value);
      }

      private static string StripVersion(string value)
      {
        if (!value.StartsWith("@"))
        {
          int at2 = value.LastIndexOf("@");
          return at2 > 0 ? value.Substring(0, at2) : value;
        }

        int slash = value.IndexOf("/");
        if (slash == -1)
          return value;

        int at = value.IndexOf("@", slash);
        return at == -1 ? value : value.Substring(0, at);
      }

      private static bool IsPathSpecifier(string value)
      {
        if (value.StartsWith("@"))
          return false;

        if (value.StartsWith(".") || value.StartsWith("/") || value.StartsWith("\\"))
          return true;

        if (PathRegex.IsMatch(value))
          return true;

        if (!value.Contains("/") && !value.Contains("\\"))
          return false;

        var normalized = value.Replace("\\", "/");
        if (normalized.Contains("/node_modules/"))
          return true;

        if (normalized.Contains("/.opencode/") || normalized.Contains("/.kilo/") || normalized.Contains("/.kilocode/"))
        {
          return true;
        }

        return FileExtensionRegex.IsMatch(normalized);
      }

      private static string NormalizePath(string value)
      {
        var parts = value.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        int idx = Array.LastIndexOf(parts, "node_modules");

        if (idx >= 0)
        {
          if (idx + 1 < parts.Length)
          {
            var head = parts[idx + 1];
            if (head.StartsWith("@"))
            {
              if (idx + 2 < parts.Length)
              {
                var tail = parts[idx + 2];
                return $"{head}/{tail}";
              }
            }
            return head;
          }
        }

        // Check for @kilocode/kilo-indexing pattern
        for (int i = 0; i < parts.Length - 1; i++)
        {
          if (parts[i] == "@kilocode" && parts[i + 1] == "kilo-indexing")
            return "@kilocode/kilo-indexing";
        }

        // Check for packages/kilo-indexing pattern
        for (int i = 0; i < parts.Length - 1; i++)
        {
          if (parts[i] == "packages" && parts[i + 1] == "kilo-indexing")
            return "@kilocode/kilo-indexing";
        }

        return Stem(value);
      }

      private static string FromFileUrl(string value)
      {
        try
        {
          var url = new Uri(value);
          var path = Uri.UnescapeDataString(url.AbsolutePath);

          if (Regex.IsMatch(path, @"^/[A-Za-z]:/"))
            return path.Substring(1);

          if (!string.IsNullOrEmpty(url.Host))
            return $"//{url.Host}{path}";

          return path;
        }
        catch
        {
          // If URL parsing fails, return the original value
          return value;
        }
      }

      private static string Stem(string value)
      {
        var parts = value.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
        var part = parts.Length > 0 ? parts[parts.Length - 1] : value;

        int dot = part.LastIndexOf(".");
        if (dot <= 0)
          return part;

        return part.Substring(0, dot);
      }
    }
  }
