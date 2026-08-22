using System;
using System.Collections.Generic;

public static class EnumHelper
{
  private static readonly Dictionary<Type, object> _cache = new();

  public static T ToEnum<T>(this string value, T defaultValue = default!) where T : Enum
  {
    var type = typeof(T);

    lock (_cache)
    {
      if (!_cache.TryGetValue(type, out var cached))
      {
        var dict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames(type))
          dict[name.ToLower()] = (T)Enum.Parse(type, name);
        _cache[type] = dict;
        cached = dict;
      }

      var res = (Dictionary<string, T>)cached;
      return res.TryGetValue(value.ToLower(), out var result) ? result : defaultValue;
    }
  }
}
