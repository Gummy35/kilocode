using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Utils
{
  internal class AsyncUtils
  {
    public static Task<string> ReadAllTextAsync(string path)
    {
      return Task.Run(() => File.ReadAllText(path));
    }

    public static Task WriteAllTextAsync(string path, string content)
    {
      return WriteAllTextAsync(path, content, Encoding.UTF8);
    }

    public static Task WriteAllTextAsync(string path, string content, Encoding encoding)
    {
      return Task.Run(() => File.WriteAllText(path, content, encoding));
    }

    public static Task<byte[]> ReadAllBytesAsync(string path)
    {
      return Task.Run(() => System.IO.File.ReadAllBytes(path));
    }

    internal static Task RemoveFileAsync(string file)
    {
      return Task.Run(() =>
      {
        try
        {
          File.Delete(file);
        }
        catch (Exception ex) {
          
        }
      });

    }

    internal static async Task<bool> SafeTask(Task<bool> r)
    {
      try { return await r; }
      catch { return false; }
    }
  }
}
