using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;

namespace KiloVisualStudioExtension.Tests
{
  public static class FixtureLoader
  {
    public static string Load(string path)
    {
      path = System.IO.Path.Combine(
        AppContext.BaseDirectory,
        "Fixtures",
        path);
      if (!File.Exists(path))
        throw new FileNotFoundException(path);
      return File.ReadAllText(path);
    }
  }
}
