using System;
using System.Reflection;

namespace KiloVisualStudioExtension.Services
{
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
