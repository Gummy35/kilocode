using System;
using System.Collections.Generic;

namespace KiloVisualStudioExtension
{
  public class ServiceProvider
  {
    private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

    public T AddService<T>(T instance) where T : class
    {
      if (instance == null)
        throw new ArgumentNullException(nameof(instance));
      _services[typeof(T)] = instance;
      return instance;
    }

    public T? GetService<T>() where T : class
    {
      if (_services.TryGetValue(typeof(T), out var service))
        return (T)service;
      return null;
    }

    public object? GetService(Type serviceType)
    {
      if (serviceType == null)
        throw new ArgumentNullException(nameof(serviceType));
      if (_services.TryGetValue(serviceType, out var service))
        return service;
      return null;
    }

    public bool IsRegistered<T>() where T : class
    {
      return _services.ContainsKey(typeof(T));
    }

    public void RemoveService<T>() where T : class
    {
      _services.Remove(typeof(T));
    }

    public void Clear()
    {
      _services.Clear();
    }
  }
}
