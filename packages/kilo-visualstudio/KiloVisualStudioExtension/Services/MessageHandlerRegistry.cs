using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Registry for message handlers that delegates webview messages to specialized handler services.
    /// This matches the VS Code pattern where message handling logic is extracted into separate
    /// handler modules (e.g., kilo-provider/handlers/auth.ts, cloud-session.ts, etc.).
    /// 
    /// The registry maintains a mapping of message types to handler actions, allowing VSProvider
    /// to stay under 1500 lines by delegating to specialized services.
    /// </summary>
    public class MessageHandlerRegistry
    {
        /// <summary>
        /// Dictionary mapping message types to their handler actions.
        /// </summary>
        private readonly Dictionary<string, Func<JsonElement?, Task>> _handlers = new Dictionary<string, Func<JsonElement?, Task>>();

        /// <summary>
        /// Registers a handler for a specific message type.
        /// </summary>
        /// <param name="messageType">The type of message to handle (e.g., "prompt", "loadMessages").</param>
        /// <param name="handler">The async handler action to invoke when the message is received.</param>
        public void Register(string messageType, Func<JsonElement?, Task> handler)
        {
            _handlers[messageType] = handler;
        }

        /// <summary>
        /// Checks if a handler is registered for the given message type.
        /// </summary>
        /// <param name="messageType">The message type to check.</param>
        /// <returns>True if a handler exists, false otherwise.</returns>
        public bool HasHandler(string messageType)
        {
            return _handlers.ContainsKey(messageType);
        }

        /// <summary>
        /// Invokes the handler for the given message type.
        /// Returns true if a handler was found and invoked, false otherwise.
        /// </summary>
        /// <param name="messageType">The type of message.</param>
        /// <param name="payload">The message payload.</param>
        /// <returns>True if handler was found, false if no handler exists.</returns>
        public async Task<bool> HandleAsync(string messageType, JsonElement? payload)
        {
            if (_handlers.TryGetValue(messageType, out var handler))
            {
                await handler(payload);
                return true;
            }
            return false;
        }

        /// <summary>
        /// Gets the count of registered handlers.
        /// </summary>
        public int HandlerCount => _handlers.Count;
    }
}
