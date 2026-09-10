using EnvDTE;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace KiloVisualStudioExtension
{
  public class ServiceProvider : IServiceProvider
  {
    private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();

    public T AddService<T>(T instance) where T : class
    {
      if (instance == null)
        throw new ArgumentNullException(nameof(instance));
      _services[typeof(T)] = instance;
      return instance;
    }

    //public T? GetService<T>() where T : class
    //{
    //  if (_services.TryGetValue(typeof(T), out var service))
    //    return (T)service;
    //  return null;
    //}

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

    public EnvDTE.DTE? GetDTE()
    {
      return ThreadHelper.JoinableTaskFactory.Run(async () =>
      {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        return (EnvDTE.DTE?)await KiloProvider.Package.GetServiceAsync(typeof(EnvDTE.DTE));
      });
    }
  }

  /// <summary>
  /// Marker interface for services that can be auto-instantiated with ServiceProvider.
  /// Services implementing this interface must have a constructor accepting ServiceProvider.
  /// </summary>
  public interface IServiceProviderService
  {
  }

  abstract public class ServiceProviderServiceBase : IServiceProviderService, IDisposable
  {
    protected readonly ServiceProvider _serviceProvider;

    public ServiceProvider ServiceProvider => _serviceProvider;

    public ServiceProviderServiceBase(ServiceProvider serviceProvider)
    {
      _serviceProvider = serviceProvider;
    }

    public void Dispose()
    {

    }
  }

  /// <summary>
  /// Extension methods for ServiceProvider to support auto-creation of IServiceProviderService instances.
  /// </summary>
  public static class ServiceProviderExtensions
  {
    /// <summary>
    /// Gets a service from the provider, or creates it automatically if not found.
    /// The service type must implement IServiceProviderService and have a constructor accepting ServiceProvider.
    /// </summary>
    public static T GetService<T>(this ServiceProvider serviceProvider)
        where T : class, IServiceProviderService
    {
      // Try to get existing service
      var serviceType = typeof(T);
      var existing = (T)serviceProvider.GetService(serviceType);
      if (existing != null)
        return existing;

      // Auto-create using reflection
      var constructor = serviceType.GetConstructor(
          BindingFlags.Public | BindingFlags.Instance,
          null,
          new[] { typeof(ServiceProvider) },
          null);

      if (constructor == null)
      {
        throw new InvalidOperationException(
            $"Type '{serviceType.Name}' must have a public constructor accepting ServiceProvider parameter to be auto-created.");
      }

      var instance = (T)constructor.Invoke(new object[] { serviceProvider });
      return instance;
    }
  }
}
