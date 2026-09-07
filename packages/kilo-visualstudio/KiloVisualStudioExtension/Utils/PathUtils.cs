using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Utils
{
  public class PathUtils
  {
    public static bool SameDirectory(string a, string b)
    {
      if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
      a = System.IO.Path.GetFullPath(a).ToLower();
      b = System.IO.Path.GetFullPath(b).ToLower();
      return a == b;
    }

    /// <summary>
    /// Computes the relative path from <paramref name="relativeTo"/> to <paramref name="path"/>.
    /// Equivalent to Path.GetRelativePath in .NET Core 2.1+.
    /// </summary>
    public static string GetRelativePath(string relativeTo, string path)
    {
      {
        if (string.IsNullOrEmpty(relativeTo))
          throw new ArgumentException("Path cannot be empty", nameof(relativeTo));
        if (string.IsNullOrEmpty(path))
          throw new ArgumentException("Path cannot be empty", nameof(path));

        relativeTo = Path.GetFullPath(relativeTo);
        path = Path.GetFullPath(path);

        // Handle different drives on Windows
        if (Path.GetPathRoot(relativeTo)?.Equals(Path.GetPathRoot(path), StringComparison.OrdinalIgnoreCase) == false)
        {
          return path;
        }

        var relativeToSegments = relativeTo.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var pathSegments = path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        // Remove empty entries
        relativeToSegments = relativeToSegments.Where(s => !string.IsNullOrEmpty(s)).ToArray();
        pathSegments = pathSegments.Where(s => !string.IsNullOrEmpty(s)).ToArray();

        // Find common prefix
        int commonLength = 0;
        int maxCompare = Math.Min(relativeToSegments.Length, pathSegments.Length);
        for (int i = 0; i < maxCompare; i++)
        {
          if (relativeToSegments[i].Equals(pathSegments[i], StringComparison.OrdinalIgnoreCase))
          {
            commonLength++;
          }
          else
          {
            break;
          }
        }

        // Build relative path
        var result = new System.Text.StringBuilder();

        // Add ".." for each segment in relativeTo that's not in the common prefix
        for (int i = commonLength; i < relativeToSegments.Length; i++)
        {
          if (result.Length > 0)
            result.Append(Path.DirectorySeparatorChar);
          result.Append("..");
        }

        // Add remaining segments from path
        for (int i = commonLength; i < pathSegments.Length; i++)
        {
          if (result.Length > 0)
            result.Append(Path.DirectorySeparatorChar);
          result.Append(pathSegments[i]);
        }

        return result.Length > 0 ? result.ToString() : ".";
      }
    }
  }
}
