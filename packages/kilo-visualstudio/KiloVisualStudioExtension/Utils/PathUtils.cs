using System;
using System.Collections.Generic;
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
  }
}
