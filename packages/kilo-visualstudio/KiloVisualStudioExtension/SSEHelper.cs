using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace KiloVisualStudioExtension
{
    public class SSEHelper
    {
        private readonly HashSet<string> _trackedSessionIds = new HashSet<string>();
        private readonly Dictionary<string, SessionStatus> _sessionStatusMap = new Dictionary<string, SessionStatus>();
        private readonly Dictionary<string, SessionRevision> _revisions = new Dictionary<string, SessionRevision>();
        private readonly HashSet<string> _modelUsageSessionIds = new HashSet<string>();
        private readonly Dictionary<string, MessageCost> _messageCosts = new Dictionary<string, MessageCost>();
        private int _sandboxRevision = 0;
        private JsonElement? _cachedIndexingStatusMessage = null;

        public string? CurrentSessionID { get; private set; }

        public ICollection<string> TrackedSessionIds => _trackedSessionIds;
        public IReadOnlyDictionary<string, SessionStatus> SessionStatusMap => _sessionStatusMap;

        private readonly Action<string> _postMessage;

        public SSEHelper(Action<string> postMessage)
        {
            _postMessage = postMessage;
        }

        public void PostMessage(object message)
        {
            _postMessage(JsonSerializer.Serialize(message));
        }

        public class SessionRevision
        {
            public long Id { get; set; }
            public int Seq { get; set; }
        }

        public class SessionStatus
        {
            public string Type { get; set; } = "";
            public int Attempt { get; set; }
            public string? Message { get; set; }
            public long? Next { get; set; }
        }

        public class MessageCost
        {
            public string SessionID { get; set; } = "";
            public string MessageID { get; set; } = "";
            public double Cost { get; set; }
        }

        public void HandleEvent(string eventType, string data)
        {
            try
            {
                var jsonData = JsonSerializer.Deserialize<JsonElement>(data);
                if (jsonData.TryGetProperty("payload", out var payload))
                    jsonData = payload;

                string? sessionID = null;
                string? directory = null;

                if (jsonData.TryGetProperty("name", out var nameProp))
                {
                    var name = nameProp.GetString() ?? "";
                    if (jsonData.TryGetProperty("id", out var idProp))
                        sessionID = idProp.GetString();
                    if (jsonData.TryGetProperty("aggregateID", out var aggProp))
                        sessionID = aggProp.GetString();
                    
                    HandleSyncEvent(name, jsonData.GetProperty("data"), jsonData.TryGetProperty("id", out var eid) ? eid.GetString() : null, jsonData.TryGetProperty("seq", out var seq) ? seq.GetInt32() : 0);
                }
                else if (jsonData.TryGetProperty("type", out var typeProp))
                {
                    var type = typeProp.GetString() ?? "";
                    if (jsonData.TryGetProperty("sessionID", out var sidProp))
                        sessionID = sidProp.GetString();
                    if (jsonData.TryGetProperty("directory", out var dirProp))
                        directory = dirProp.GetString();
                    
                    HandleStreamEvent(type, jsonData.GetProperty("properties"), sessionID, directory);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: error handling SSE event: {ex.Message} / {data}");
            }
        }

        private void HandleSyncEvent(string name, JsonElement data, string? eventId, int seq)
        {
            switch (name)
            {
                case "message.updated.1":
                    HandleMessageUpdatedSync(data);
                    break;
                case "message.removed.1":
                    HandleMessageRemovedSync(data);
                    break;
                case "message.part.updated.1":
                    HandlePartUpdatedSync(data);
                    break;
                case "message.part.removed.1":
                    HandlePartRemovedSync(data);
                    break;
                case "session.created.1":
                    HandleSessionCreatedSync(data);
                    break;
                case "session.updated.1":
                    HandleSessionUpdatedSync(data, eventId, seq);
                    break;
                case "session.deleted.1":
                    HandleSessionDeletedSync(data);
                    break;
            }
        }

        private void HandleStreamEvent(string type, JsonElement properties, string? sessionID, string? directory)
        {
            switch (type)
            {
                case "kilo-sessions.remote-status-changed":
                    return;

                case "memory.status":
                case "memory.updated":
                case "memory.error":
                    HandleMemoryEvent(type, properties);
                    return;

                case "session.status":
                    HandleSessionStatus(properties, sessionID);
                    return;

                case "message.part.delta":
                    HandlePartDelta(properties);
                    return;

                case "session.created":
                    HandleSessionCreatedStream(properties);
                    break;

                case "session.updated":
                    HandleSessionUpdatedStream(properties);
                    break;

                case "session.deleted":
                    HandleSessionDeletedStream(properties);
                    break;

                case "message.updated":
                    HandleMessageUpdatedStream(properties);
                    break;

                case "message.removed":
                    HandleMessageRemovedStream(properties);
                    break;

                case "global.disposed":
                    HandleGlobalDisposed();
                    return;

                case "server.instance.disposed":
                    HandleServerInstanceDisposed(properties);
                    return;

                case "global.config.updated":
                    HandleGlobalConfigUpdated();
                    return;

                case "message.part.updated":
                    HandlePartUpdatedStream(properties);
                    break;

                case "indexing.status":
                    HandleIndexingStatus(properties);
                    break;

                case "session.turn.close":
                    HandleSessionTurnClosed(properties);
                    break;

                case "permission.asked":
                    HandlePermissionAsked(properties);
                    break;

                case "permission.replied":
                    HandlePermissionReplied(properties);
                    break;

                case "todo.updated":
                    HandleTodoUpdated(properties);
                    break;

                case "question.asked":
                    HandleQuestionAsked(properties);
                    break;

                case "question.replied":
                case "question.rejected":
                    HandleQuestionResolved(properties);
                    break;

                case "suggestion.shown":
                    HandleSuggestionShown(properties);
                    break;

                case "suggestion.accepted":
                case "suggestion.dismissed":
                    HandleSuggestionResolved(properties);
                    break;

                case "session.error":
                    HandleSessionError(properties);
                    break;

                case "sandbox.status.changed":
                    HandleSandboxStatusChanged(properties);
                    break;
            }
        }

        private void HandleMessageUpdatedSync(JsonElement data)
        {
            var info = data.GetProperty("info");
            var sessionID = info.GetProperty("sessionID").GetString();
            var messageID = info.GetProperty("id").GetString();

            if (info.TryGetProperty("cost", out var costProp) && costProp.ValueKind == JsonValueKind.Number)
            {
                var cost = costProp.GetDouble();
                if (info.GetProperty("role").GetString() == "assistant")
                {
                    _messageCosts[messageID] = new MessageCost { SessionID = sessionID ?? "", MessageID = messageID, Cost = cost };
                }
            }

            var createdAt = info.TryGetProperty("time", out var time) && time.TryGetProperty("created", out var created)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)created.GetDouble()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            // Match TypeScript: { ...info, createdAt: new Date(info.time.created).toISOString() }
            // We need to copy all properties from info and add createdAt
            var messageObj = new Dictionary<string, object?>();
            foreach (var prop in info.EnumerateObject())
            {
                messageObj[prop.Name] = prop.Value;
            }
            messageObj["createdAt"] = createdAt;
            
            PostMessage(new
            {
                type = "messageCreated",
                message = messageObj
            });
        }

        private void HandleMessageRemovedSync(JsonElement data)
        {
            var sessionID = data.GetProperty("sessionID").GetString();
            var messageID = data.GetProperty("messageID").GetString();
            
            _messageCosts.Remove(messageID);

            PostMessage(new
            {
                type = "messageRemoved",
                sessionID,
                messageID
            });
        }

        private void HandlePartUpdatedSync(JsonElement data)
        {
            var sessionID = data.GetProperty("sessionID").GetString();
            var part = data.GetProperty("part");
            var messageID = part.GetProperty("messageID").GetString();
            
            if (part.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("sessionId", out var childIdProp))
            {
                var childId = childIdProp.GetString();
                if (!string.IsNullOrEmpty(childId) && !_trackedSessionIds.Contains(childId))
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Auto-adopting child session: {childId}");
                    _trackedSessionIds.Add(childId);
                }
            }

            PostMessage(new
            {
                type = "partUpdated",
                sessionID,
                messageID,
                part
            });
        }

        private void HandlePartRemovedSync(JsonElement data)
        {
            var sessionID = data.GetProperty("sessionID").GetString();
            var messageID = data.GetProperty("messageID").GetString();
            var partID = data.GetProperty("partID").GetString();
            
            PostMessage(new
            {
                type = "partRemoved",
                sessionID,
                messageID,
                partID
            });
        }

        private void HandleSessionCreatedSync(JsonElement data)
        {
            var info = data.GetProperty("info");
            var sessionID = info.GetProperty("id").GetString();
            
            if (string.IsNullOrEmpty(CurrentSessionID))
            {
                CurrentSessionID = sessionID;
                _trackedSessionIds.Add(sessionID);
            }

            var time = info.GetProperty("time");
            var createdAt = time.TryGetProperty("created", out var created)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)created.GetDouble()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            var updatedAt = time.TryGetProperty("updated", out var updated)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)updated.GetDouble()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            PostMessage(new
            {
                type = "sessionCreated",
                session = new
                {
                    id = sessionID,
                    parentID = info.TryGetProperty("parentID", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                    title = info.GetProperty("title").GetString(),
                    createdAt,
                    updatedAt,
                    revert = (object?) (info.TryGetProperty("revert", out var r) && r.ValueKind == JsonValueKind.Object ? r : null),
                    summary = info.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null
                }
            });
        }

        private void HandleSessionUpdatedSync(JsonElement data, string? eventId, int seq)
        {
            var sessionID = data.GetProperty("sessionID").GetString();
            var info = data.GetProperty("info");
            
            if (info.TryGetProperty("cost", out var costProp) && costProp.ValueKind == JsonValueKind.Number)
            {
                // requestCostAlert - not implemented
            }

            if (!string.IsNullOrEmpty(eventId))
            {
                var revision = new SessionRevision { Id = long.Parse(eventId), Seq = seq };
                _revisions[sessionID] = revision;
            }

            if (CurrentSessionID == sessionID)
            {
                CurrentSessionID = sessionID;
            }

            var time = info.GetProperty("time");
            var createdAt = time.TryGetProperty("created", out var created)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)created.GetDouble()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            var updatedAt = time.TryGetProperty("updated", out var updated)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)updated.GetDouble()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            PostMessage(new
            {
                type = "sessionUpdated",
                session = new
                {
                    id = sessionID,
                    parentID = info.TryGetProperty("parentID", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                    title = info.GetProperty("title").GetString(),
                    createdAt,
                    updatedAt,
                    revert = (object?) (info.TryGetProperty("revert", out var r) && r.ValueKind == JsonValueKind.Object ? r : null),
                    summary = info.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null
                }
            });
        }

        private void HandleSessionDeletedSync(JsonElement data)
        {
            var sessionID = data.GetProperty("sessionID").GetString();
            
            _trackedSessionIds.Remove(sessionID);
            _modelUsageSessionIds.Remove(sessionID);
            _revisions.Remove(sessionID);
            _sessionStatusMap.Remove(sessionID);
            
            var costsToRemove = _messageCosts.Where(kvp => kvp.Value.SessionID == sessionID).Select(kvp => kvp.Key).ToArray();
            foreach (var costId in costsToRemove)
            {
                _messageCosts.Remove(costId);
            }

            PostMessage(new
            {
                type = "sessionDeleted",
                sessionID
            });
        }

        private void HandleMemoryEvent(string type, JsonElement properties)
        {
            var eventSessionID = properties.TryGetProperty("sessionID", out var sid) ? sid.GetString() : null;
            var active = CurrentSessionID;
            
            var local = string.IsNullOrEmpty(eventSessionID) || eventSessionID == active || _trackedSessionIds.Contains(eventSessionID);
            if (!local) return;

            object? detail = null;
            if (properties.TryGetProperty("detail", out var detailProp) && detailProp.ValueKind == JsonValueKind.Object)
            {
                detail = detailProp;
            }
            else if (type == "memory.error" && properties.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String)
            {
                detail = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new { type = "error", message = reason.GetString(), reason = reason.GetString() }));
            }

            if (detail != null)
            {
                PostMessage(new
                {
                    type = "memoryEvent",
                    sessionID = eventSessionID,
                    detail = detail
                });
            }
        }

        private void HandleSessionStatus(JsonElement properties, string? sessionID)
        {
            var status = properties.GetProperty("status");
            var statusType = status.GetProperty("type").GetString();
            var sid = properties.GetProperty("sessionID").GetString();
            
            var prev = _sessionStatusMap.ContainsKey(sid) ? _sessionStatusMap[sid] : null;
            if ((prev == null || prev.Type == "idle") && statusType != "idle")
            {
                // costs.rearm(sid) - not implemented
            }
            
            _sessionStatusMap[sid] = new SessionStatus
            {
                Type = statusType ?? "",
                Attempt = status.TryGetProperty("attempt", out var attempt) ? attempt.GetInt32() : 0,
                Message = status.TryGetProperty("message", out var msg) ? msg.GetString() : null,
                Next = status.TryGetProperty("next", out var next) && next.ValueKind == JsonValueKind.Number ? (long?)next.GetInt64() : null
            };

            object extra;
            if (statusType == "retry") 
                extra = new { attempt = _sessionStatusMap[sid].Attempt, message = _sessionStatusMap[sid].Message, next = _sessionStatusMap[sid].Next };
            else if (statusType == "offline")
                extra = new { message = _sessionStatusMap[sid].Message };
            else
                extra = new { };

            PostMessage(new
            {
                type = "sessionStatus",
                sessionID = sid,
                status = statusType,
                extra
            });
        }

        private void HandlePartDelta(JsonElement properties)
        {
            var partID = properties.GetProperty("partID").GetString();
            var messageID = properties.GetProperty("messageID").GetString();
            var sid = properties.GetProperty("sessionID").GetString();
            var delta = properties.GetProperty("delta").GetString();
            
            if (!string.IsNullOrEmpty(sid) && !_trackedSessionIds.Contains(sid))
                return;

            PostMessage(new
            {
                type = "partUpdated",
                sessionID = sid,
                messageID,
                part = new { id = partID, type = "text", messageID, text = delta },
                delta = new { type = "text-delta", textDelta = delta }
            });
        }

        private void HandleSessionCreatedStream(JsonElement properties)
        {
            var info = properties.GetProperty("info");
            var sessionID = info.GetProperty("id").GetString();
            
            if (string.IsNullOrEmpty(CurrentSessionID))
            {
                CurrentSessionID = sessionID;
                _trackedSessionIds.Add(sessionID);
            }

            var time = info.GetProperty("time");
            var createdAt = time.TryGetProperty("created", out var created)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)created.GetDouble()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            var updatedAt = time.TryGetProperty("updated", out var updated)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)updated.GetDouble()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            PostMessage(new
            {
                type = "sessionCreated",
                session = new
                {
                    id = sessionID,
                    parentID = info.TryGetProperty("parentID", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                    title = info.GetProperty("title").GetString(),
                    createdAt,
                    updatedAt,
                    revert = (object?) (info.TryGetProperty("revert", out var r) && r.ValueKind == JsonValueKind.Object ? r : null),
                    summary = info.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null
                }
            });
        }

        private void HandleSessionUpdatedStream(JsonElement properties)
        {
            var sessionID = properties.GetProperty("sessionID").GetString();
            var info = properties.GetProperty("info");
            
            if (info.TryGetProperty("cost", out var costProp) && costProp.ValueKind == JsonValueKind.Number)
            {
                // requestCostAlert - not implemented
            }

            if (CurrentSessionID == sessionID)
            {
                CurrentSessionID = sessionID;
            }

            var time = info.GetProperty("time");
            var createdAt = time.TryGetProperty("created", out var created)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)created.GetDouble()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            var updatedAt = time.TryGetProperty("updated", out var updated)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)updated.GetDouble()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            PostMessage(new
            {
                type = "sessionUpdated",
                session = new
                {
                    id = sessionID,
                    parentID = info.TryGetProperty("parentID", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null,
                    title = info.GetProperty("title").GetString(),
                    createdAt,
                    updatedAt,
                    revert = (object?) (info.TryGetProperty("revert", out var r) && r.ValueKind == JsonValueKind.Object ? r : null),
                    summary = info.TryGetProperty("summary", out var s) && s.ValueKind == JsonValueKind.String ? s.GetString() : null
                }
            });
        }

        private void HandleSessionDeletedStream(JsonElement properties)
        {
            var sessionID = properties.GetProperty("sessionID").GetString();
            
            _trackedSessionIds.Remove(sessionID);
            _modelUsageSessionIds.Remove(sessionID);
            _revisions.Remove(sessionID);
            _sessionStatusMap.Remove(sessionID);
            
            var costsToRemove = _messageCosts.Where(kvp => kvp.Value.SessionID == sessionID).Select(kvp => kvp.Key).ToArray();
            foreach (var costId in costsToRemove)
            {
                _messageCosts.Remove(costId);
            }

            PostMessage(new
            {
                type = "sessionDeleted",
                sessionID
            });
        }

        private void HandleMessageUpdatedStream(JsonElement properties)
        {
            var info = properties.GetProperty("info");
            var sessionID = info.GetProperty("sessionID").GetString();
            var messageID = info.GetProperty("id").GetString();
            
            if (info.TryGetProperty("cost", out var costProp) && costProp.ValueKind == JsonValueKind.Number)
            {
                var cost = costProp.GetDouble();
                if (info.GetProperty("role").GetString() == "assistant")
                {
                    _messageCosts[messageID] = new MessageCost { SessionID = sessionID, MessageID = messageID, Cost = cost };
                }
            }

            var createdAt = info.TryGetProperty("time", out var time) && time.TryGetProperty("created", out var created)
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)created.GetInt64()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            // Match TypeScript: { ...info, createdAt: new Date(info.time.created).toISOString() }
            var messageObj = new Dictionary<string, object?>();
            foreach (var prop in info.EnumerateObject())
            {
                messageObj[prop.Name] = prop.Value;
            }
            messageObj["createdAt"] = createdAt;
            
            PostMessage(new
            {
                type = "messageCreated",
                message = messageObj
            });
        }

        private void HandleMessageRemovedStream(JsonElement properties)
        {
            var messageID = properties.GetProperty("messageID").GetString();
            _messageCosts.Remove(messageID);

            PostMessage(new
            {
                type = "messageRemoved",
                sessionID = properties.GetProperty("sessionID").GetString(),
                messageID
            });
        }

        private void HandleGlobalDisposed()
        {
            PostMessage(new { type = "globalDisposed" });
        }

        private void HandleServerInstanceDisposed(JsonElement properties)
        {
            var dir = properties.TryGetProperty("directory", out var dirProp) ? dirProp.GetString() : null;
            
            foreach (var sid in _sessionStatusMap.Keys.ToList())
            {
                _sessionStatusMap[sid] = new SessionStatus { Type = "idle" };
            }

            PostMessage(new { type = "serverInstanceDisposed", directory = dir });
        }

        private void HandleGlobalConfigUpdated()
        {
            PostMessage(new { type = "globalConfigUpdated" });
        }

        private void HandlePartUpdatedStream(JsonElement properties)
        {
            var part = properties.GetProperty("part");
            var sessionID = properties.GetProperty("sessionID").GetString();
            var messageID = part.GetProperty("messageID").GetString();
            
            if (part.TryGetProperty("metadata", out var metadata) && metadata.TryGetProperty("sessionId", out var childIdProp))
            {
                var childId = childIdProp.GetString();
                if (!string.IsNullOrEmpty(childId) && !_trackedSessionIds.Contains(childId))
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Auto-adopting child session: {childId}");
                    _trackedSessionIds.Add(childId);
                }
            }

            PostMessage(new
            {
                type = "partUpdated",
                sessionID,
                messageID,
                part
            });
        }

        private void HandleIndexingStatus(JsonElement properties)
        {
            var status = properties.GetProperty("status");
            _cachedIndexingStatusMessage = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(new { type = "indexingStatusLoaded", status }));
            
            if (_cachedIndexingStatusMessage.HasValue)
            {
                PostMessage(_cachedIndexingStatusMessage.Value.GetRawText());
            }
        }

        private void HandleSessionTurnClosed(JsonElement properties)
        {
            PostMessage(new
            {
                type = "sessionTurnClosed",
                sessionID = properties.GetProperty("sessionID").GetString(),
                reason = properties.GetProperty("reason").GetString()
            });
        }

        private void HandlePermissionAsked(JsonElement properties)
        {
            var permission = properties.GetProperty("permission").GetString();
            PostMessage(new
            {
                type = "permissionRequest",
                permission = new
                {
                    id = properties.GetProperty("id").GetString(),
                    sessionID = properties.GetProperty("sessionID").GetString(),
                    toolName = permission,
                    patterns = properties.TryGetProperty("patterns", out var p) ? p : JsonDocument.Parse("[]").RootElement,
                    always = properties.TryGetProperty("always", out var a) ? a : JsonDocument.Parse("[]").RootElement,
                    args = properties.TryGetProperty("metadata", out var m) ? m : JsonDocument.Parse("{}").RootElement,
                    message = $"Permission required: {permission}",
                    tool = properties.TryGetProperty("tool", out var t) ? t : JsonDocument.Parse("{}").RootElement
                }
            });
        }

        private void HandlePermissionReplied(JsonElement properties)
        {
            PostMessage(new
            {
                type = "permissionResolved",
                permissionID = properties.GetProperty("requestID").GetString()
            });
        }

        private void HandleTodoUpdated(JsonElement properties)
        {
            PostMessage(new
            {
                type = "todoUpdated",
                sessionID = properties.GetProperty("sessionID").GetString(),
                items = properties.GetProperty("todos")
            });
        }

        private void HandleQuestionAsked(JsonElement properties)
        {
            PostMessage(new
            {
                type = "questionRequest",
                question = new
                {
                    id = properties.GetProperty("id").GetString(),
                    sessionID = properties.GetProperty("sessionID").GetString(),
                    questions = properties.GetProperty("questions"),
                    blocking = properties.TryGetProperty("blocking", out var b) ? b.GetBoolean() : false,
                    tool = properties.TryGetProperty("tool", out var t) ? t : JsonDocument.Parse("{}").RootElement
                }
            });
        }

        private void HandleQuestionResolved(JsonElement properties)
        {
            PostMessage(new
            {
                type = "questionResolved",
                requestID = properties.GetProperty("requestID").GetString()
            });
        }

        private void HandleSuggestionShown(JsonElement properties)
        {
            PostMessage(new
            {
                type = "suggestionRequest",
                suggestion = new
                {
                    id = properties.GetProperty("id").GetString(),
                    sessionID = properties.GetProperty("sessionID").GetString(),
                    text = properties.GetProperty("text").GetString(),
                    actions = properties.GetProperty("actions"),
                    blocking = properties.TryGetProperty("blocking", out var b) ? b.GetBoolean() : false,
                    tool = properties.TryGetProperty("tool", out var t) ? t : JsonDocument.Parse("{}").RootElement
                }
            });
        }

        private void HandleSuggestionResolved(JsonElement properties)
        {
            PostMessage(new
            {
                type = "suggestionResolved",
                requestID = properties.GetProperty("requestID").GetString()
            });
        }

        private void HandleSessionError(JsonElement properties)
        {
            PostMessage(new
            {
                type = "sessionError",
                sessionID = properties.TryGetProperty("sessionID", out var sid) ? sid.GetString() : null,
                error = properties.TryGetProperty("error", out var e) ? e : JsonDocument.Parse("{}").RootElement
            });
        }

        private void HandleSandboxStatusChanged(JsonElement properties)
        {
            _sandboxRevision++;
            PostMessage(new
            {
                type = "sandboxStatus",
                sessionID = properties.GetProperty("sessionID").GetString(),
                directory = properties.GetProperty("directory").GetString(),
                enabled = properties.GetProperty("enabled").GetBoolean(),
                available = properties.GetProperty("available").GetBoolean(),
                reason = properties.TryGetProperty("reason", out var r) ? r.GetString() : null,
                version = properties.GetProperty("version").GetInt32(),
                revision = _sandboxRevision
            });
        }

        public void TrackSession(string sessionID)
        {
            _trackedSessionIds.Add(sessionID);
        }

        public void UntrackSession(string sessionID)
        {
            _trackedSessionIds.Remove(sessionID);
        }

        public bool IsSessionTracked(string sessionID)
        {
            return _trackedSessionIds.Contains(sessionID);
        }

        public void SetCurrentSession(string sessionID)
        {
            CurrentSessionID = sessionID;
        }
    }
}
