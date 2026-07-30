using System;
using System.Collections.Generic;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Represents a queued session request.
    /// </summary>
    public class QueuedRequest
    {
        public string SessionId { get; set; } = "";
        public string Type { get; set; } = "";
        public DateTime EnqueuedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// FIFO queue for session requests.
    /// </summary>
    public class SessionQueue
    {
        private readonly Queue<QueuedRequest> _queue = new Queue<QueuedRequest>();
        public List<QueuedRequest> Processed { get; } = new List<QueuedRequest>();

        public void Enqueue(QueuedRequest request) => _queue.Enqueue(request);

        public bool Dequeue(out QueuedRequest? request)
        {
            if (_queue.Count > 0)
            {
                request = _queue.Dequeue();
                Processed.Add(request);
                return true;
            }
            request = null;
            return false;
        }

        public int Count => _queue.Count;
    }
}
