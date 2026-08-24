using System;
using System.Threading.Tasks;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension.Services.Handlers.Sandbox
{
    /// <summary>
    /// Handles sandbox revision state.
    /// </summary>
    public class SandboxHandlerService : ServiceProviderServiceBase
  {
        private bool _disposed;

        // Sandbox revision - moved from SSEHelper
        private int _sandboxRevision = 0;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new SandboxHandlerService instance.
        /// </summary>
        public SandboxHandlerService(ServiceProvider serviceProvider): base(serviceProvider)
        {
        }

        /// <summary>
        /// Gets the current sandbox revision.
        /// </summary>
        public int GetSandboxRevision()
        {
            return _sandboxRevision;
        }

        /// <summary>
        /// Increments and returns the sandbox revision.
        /// </summary>
        public int IncrementSandboxRevision()
        {
            return ++_sandboxRevision;
        }

        /// <summary>
        /// Sets the sandbox revision.
        /// </summary>
        public void SetSandboxRevision(int revision)
        {
            _sandboxRevision = revision;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
