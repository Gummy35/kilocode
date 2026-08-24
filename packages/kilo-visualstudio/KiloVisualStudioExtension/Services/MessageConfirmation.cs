using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Message confirmation state for tracking webview message delivery.
    /// Tracks which messages have been confirmed by the webview and allows
    /// async waiting for confirmation with timeout.
    /// Matches the VS Code MessageConfirmation class from kilo-provider-utils.ts:122-172.
    /// </summary>
    public class MessageConfirmation : ServiceProviderServiceBase
  {
        private readonly Dictionary<string, Entry> _ids = new Dictionary<string, Entry>();

    public MessageConfirmation(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }

    private class Entry
        {
            public bool Confirmed { get; set; }
            public HashSet<Action> Waits { get; set; } = new HashSet<Action>();
        }

        /// <summary>
        /// Start tracking a message ID for confirmation.
        /// Returns a cleanup action that removes the entry when invoked.
        /// Matches the VS Code track() pattern from kilo-provider-utils.ts:125-132.
        /// </summary>
        public Action Track(string? id)
        {
            if (id == null) return () => { };
            
            if (!_ids.TryGetValue(id, out var entry))
            {
                entry = new Entry();
                _ids[id] = entry;
            }
            
            return () => { _ids.Remove(id); };
        }

        /// <summary>
        /// Confirm that a message has been received by the webview.
        /// Resolves all waiters for this message.
        /// Matches the VS Code confirm() pattern from kilo-provider-utils.ts:134-141.
        /// </summary>
        public void Confirm(string id)
        {
            if (!_ids.TryGetValue(id, out var entry)) return;
            
            entry.Confirmed = true;
            var waitsCopy = new List<Action>(entry.Waits);
            foreach (var done in waitsCopy)
            {
                done();
            }
        }

        /// <summary>
        /// Check if a message has been confirmed.
        /// Matches the VS Code has() pattern from kilo-provider-utils.ts:143-146.
        /// </summary>
        public bool Has(string? id)
        {
            if (id == null) return false;
            return _ids.TryGetValue(id, out var entry) && entry.Confirmed;
        }

        /// <summary>
        /// Wait for a message to be confirmed, with timeout (default 1500ms).
        /// Returns true if confirmed, false if timeout expires.
        /// Matches the VS Code wait() pattern from kilo-provider-utils.ts:148-172.
        /// </summary>
        public Task<bool> Wait(string? id, int timeoutMs = 1500)
        {
            if (id == null) return Task.FromResult(false);
            
            if (!_ids.TryGetValue(id, out var entry)) return Task.FromResult(false);
            if (entry.Confirmed) return Task.FromResult(true);

            var tcs = new TaskCompletionSource<bool>();
            
            Action cleanup = null!;
            Action done = null!;
            
            var timer = new System.Threading.Timer(
                _ =>
                {
                    cleanup();
                    if (!tcs.Task.IsCompleted)
                        tcs.TrySetResult(entry.Confirmed);
                },
                null,
                timeoutMs,
                System.Threading.Timeout.Infinite
            );
            
            cleanup = () =>
            {
                timer.Dispose();
                entry.Waits.Remove(done);
            };

            done = () =>
            {
                cleanup();
                if (!tcs.Task.IsCompleted)
                    tcs.TrySetResult(true);
            };

            entry.Waits.Add(done);
            
            return tcs.Task;
        }
    }
}
