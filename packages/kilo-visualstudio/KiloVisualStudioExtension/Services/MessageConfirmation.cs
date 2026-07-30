using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Message confirmation state for tracking webview message delivery.
    /// Tracks which messages have been confirmed by the webview and allows
    /// async waiting for confirmation with timeout.
    /// </summary>
    public class MessageConfirmation
    {
        private readonly HashSet<string> _tracked = new HashSet<string>();
        private readonly HashSet<string> _confirmed = new HashSet<string>();
        private readonly Dictionary<string, List<TaskCompletionSource<bool>>> _waiters = new Dictionary<string, List<TaskCompletionSource<bool>>>();

        /// <summary>
        /// Start tracking a message ID for confirmation.
        /// </summary>
        public void Track(string messageId)
        {
            _tracked.Add(messageId);
            _waiters[messageId] = new List<TaskCompletionSource<bool>>();
        }

        /// <summary>
        /// Confirm that a message has been received by the webview.
        /// Resolves all waiters for this message.
        /// </summary>
        public void Confirm(string messageId)
        {
            if (_tracked.Contains(messageId))
            {
                _confirmed.Add(messageId);
                if (_waiters.TryGetValue(messageId, out var sources))
                {
                    foreach (var source in sources)
                        source.SetResult(true);
                    sources.Clear();
                }
            }
        }

        /// <summary>
        /// Check if a message has been confirmed.
        /// </summary>
        public bool Has(string messageId) => _confirmed.Contains(messageId);

        /// <summary>
        /// Wait for a message to be confirmed, with timeout.
        /// Returns true if confirmed, false if timeout expires.
        /// </summary>
        public async Task<bool> Wait(string messageId, int timeoutMs)
        {
            if (_confirmed.Contains(messageId))
                return true;

            var tcs = new TaskCompletionSource<bool>();
            if (_waiters.TryGetValue(messageId, out var sources))
                sources.Add(tcs);

            var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeoutMs));
            return completed == tcs.Task;
        }

        /// <summary>
        /// Release tracking for a message, cleaning up all associated state.
        /// </summary>
        public void Release(string messageId)
        {
            _tracked.Remove(messageId);
            _confirmed.Remove(messageId);
            _waiters.Remove(messageId);
        }
    }
}
