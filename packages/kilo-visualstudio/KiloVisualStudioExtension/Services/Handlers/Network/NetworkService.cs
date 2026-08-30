using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension.Services.Handlers.Network
{
    /// <summary>
    /// Handles network wait tracking state.
    /// </summary>
    public class NetworkService : ServiceProviderServiceBase
  {
        private bool _disposed;

        // Network wait tracking - moved from SSEHelper
        private readonly Dictionary<string, string> _networkWaits = new();

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new NetworkHandlerService instance.
        /// </summary>
        public NetworkService(ServiceProvider serviceProvider): base(serviceProvider)
        {
        }

        /// <summary>
        /// Starts tracking a network wait.
        /// </summary>
        public void StartNetworkWait(string requestID, string sessionID)
        {
            _networkWaits[requestID] = sessionID ?? "";
        }

        /// <summary>
        /// Ends tracking a network wait and returns the session ID.
        /// </summary>
        public string? EndNetworkWait(string requestID)
        {
            if (_networkWaits.TryGetValue(requestID, out var sessionID))
            {
                _networkWaits.Remove(requestID);
                return sessionID;
            }
            return null;
        }

        /// <summary>
        /// Removes a network wait entry.
        /// </summary>
        public void RemoveNetworkWait(string requestID)
        {
            _networkWaits.Remove(requestID);
        }

        /// <summary>
        /// Gets the session ID for a network wait.
        /// </summary>
        public string? GetSessionIdForNetworkWait(string requestID)
        {
            _networkWaits.TryGetValue(requestID, out var sessionID);
            return sessionID;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }
    }
}
