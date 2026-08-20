using System;
using System.Collections.Generic;
using System.Linq;

public static class EnumHelper
{
  private static readonly Dictionary<Type, Dictionary<string, T>> _cache = new();

  public static T ToEnum<T>(this string value, T defaultValue = default!) where T : Enum
  {
    var type = typeof(T);

    lock (_cache)
    {
      if (!_cache.TryGetValue(type, out var dict))
      {
        dict = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in Enum.GetNames(type))
          dict[name.ToLower()] = (T)Enum.Parse(type, name);
        _cache[type] = dict;
      }

      return dict.TryGetValue(value.ToLower(), out var result) ? result : defaultValue;
    }
  }
}
