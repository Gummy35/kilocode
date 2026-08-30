using KiloVisualStudioExtension.ApiClient;
using System;
using System.Text.RegularExpressions;

namespace KiloVisualStudioExtension.Utils
{
  /// <summary>
  /// Utility methods for generating native tab titles.
  /// Matches the TypeScript implementation in native-tab-title.ts.
  /// </summary>
  public static class NativeTabTitle
  {
    private static readonly Regex DefaultSessionTitleRegex = new Regex(
        @"^(New session|Child session) - \d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}Z$",
        RegexOptions.Compiled
    );

    private const int TitleLimit = 19;
    private const string ExtensionDisplayName = "Kilo Code";

    /// <summary>
    /// Generates a native tab title from a session.
    /// Returns the extension display name if the session has no meaningful title.
    /// Truncates long titles with ellipsis.
    /// </summary>
    /// <param name="session">The session to generate a title for.</param>
    /// <returns>The formatted title string.</returns>
    public static string GetTitle(Session? session)
    {
      var title = session?.Title?.Trim();

      if (string.IsNullOrEmpty(title))
        return ExtensionDisplayName;

      if (DefaultSessionTitleRegex.IsMatch(title))
        return ExtensionDisplayName;

      if (title.Length <= TitleLimit)
        return title;

      return $"{title.Substring(0, TitleLimit)}...";
    }
  }
}
