using System;
using System.Threading.Tasks;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension.Services.Handlers.Indexing
{
    /// <summary>
    /// Handles indexing status state.
    /// </summary>
    public class IndexingHandlerService : ServiceProviderServiceBase
  {
        private bool _disposed;

        // Cached indexing status - moved from SSEHelper
        private string? _cachedIndexingStatusMessage;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new IndexingHandlerService instance.
        /// </summary>
        public IndexingHandlerService(ServiceProvider serviceProvider): base (serviceProvider) 
        { }
        
        /// <summary>
        /// Gets the cached indexing status message.
        /// </summary>
        public string? GetCachedIndexingStatusMessage()
        {
            return _cachedIndexingStatusMessage;
        }

        /// <summary>
        /// Sets the cached indexing status message.
        /// </summary>
        public void SetCachedIndexingStatusMessage(string? message)
        {
            _cachedIndexingStatusMessage = message;
        }

        /// <summary>
        /// Clears the cached indexing status message.
        /// </summary>
        public void ClearCachedIndexingStatusMessage()
        {
            _cachedIndexingStatusMessage = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
