using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Utils
{  
  internal class Followup
  {
    const long TTL = 30000;
    const string LABEL = "Start new session";

    public string Dir { get; set; }

    public long Time { get; set; }

    public static Followup RecordFollowup(string[][] answers, string dir, long time)
    {
      var answer = answers[0]?[0]?.Trim();
      if (answer != LABEL) return null;
      return new Followup { Dir = dir, Time = time };
    }

    public static string NormalizePath(string p)
    {
      var normalized = p.Replace("\\", "/").TrimEnd('/');
      if (normalized.Length >= 2 && char.IsLetter(normalized[0]) && normalized[1] == ':')
        return normalized.ToLower();
      return normalized;
    }

    public static bool MatchesFollowup(Followup pending, string dir, long now, string parentId)
    {
      if (!string.IsNullOrEmpty(parentId)) return false;
      if (pending == null) return false;
      if (now - pending.Time > TTL) return false;
      return NormalizePath(pending.Dir) == NormalizePath(dir);
    }
  }
}
