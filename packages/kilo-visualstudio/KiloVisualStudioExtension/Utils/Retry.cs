using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static KiloVisualStudioExtension.Services.MessagePageFetcher;

namespace KiloVisualStudioExtension.Utils
{
  internal class Retry
  {
    public static async Task<T> RetryAsync<T>(Func<Task<T>> operation, int attempts = 3, int delayMs = 500)
    {
      Exception? lastException = null;

      for (int i = 0; i < attempts; i++)
      {
        try
        {
          return await operation();
        }
        catch (Exception ex)
        {
          lastException = ex;

          if (i == attempts - 1 || !IsTransientError(ex))
            throw;

          await Task.Delay(delayMs * (int)Math.Pow(2, i));
        }
      }

      throw lastException ?? new Exception("Retry failed");
    }

    private static bool IsTransientError(Exception ex)
    {
      var transientErrors = new[]
      {
        "load failed",
        "network connection was lost",
        "network request failed",
        "failed to fetch",
        "fetch failed",
        "econnreset",
        "econnrefused",
        "etimedout",
        "socket hang up"
      };

      var message = ex.Message?.ToLowerInvariant() ?? "";
      return transientErrors.Any(e => message.Contains(e));
    }
  }
}
