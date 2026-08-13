using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using KiloVisualStudioExtension.ApiClient.Json;
using KiloVisualStudioExtension.ApiClient.Sse;
using KiloVisualStudioExtension.WebView.Generated;
using ApiMessage = KiloVisualStudioExtension.ApiClient.Message;
using WebViewMessage = KiloVisualStudioExtension.WebView.Generated.Message;

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
        private string? _cachedIndexingStatusMessage = null;

        public string? CurrentSessionID { get; private set; }

        public ICollection<string> TrackedSessionIds => _trackedSessionIds;
        public IReadOnlyDictionary<string, SessionStatus> SessionStatusMap => _sessionStatusMap;

        private readonly Action<string> _postMessage;
        private readonly JsonSerializer _serializer;

        public SSEHelper(Action<string> postMessage)
        {
            _postMessage = postMessage;
            _serializer = KiloJsonSerializer.Create();
        }

        public void PostMessage(object message)
        {
            _postMessage(JsonConvert.SerializeObject(message));
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
                var sseEvent = SseEventDeserializer.Deserialize(eventType, data);

                if (sseEvent is SyncEvent syncEvent)
                {
                    HandleSyncEvent(syncEvent);
                }
                else if (sseEvent is StreamEvent streamEvent)
                {
                    HandleStreamEvent(streamEvent);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: error handling SSE event: {ex.Message}");
            }
        }

        private void HandleSyncEvent(SyncEvent syncEvent)
        {
            var name = syncEvent.Name;

            switch (name)
            {
                case "message.updated.1":
                    HandleMessageUpdatedSync((MessageUpdatedSyncEvent)syncEvent);
                    break;
                case "message.removed.1":
                    HandleMessageRemovedSync(syncEvent);
                    break;
                case "message.part.updated.1":
                    HandlePartUpdatedSync((MessagePartUpdatedSyncEvent)syncEvent);
                    break;
                case "message.part.removed.1":
                    HandlePartRemovedSync(syncEvent);
                    break;
                case "session.created.1":
                    HandleSessionCreatedSync((SessionCreatedSyncEvent)syncEvent);
                    break;
                case "session.updated.1":
                    HandleSessionUpdatedSync((SessionUpdatedSyncEvent)syncEvent);
                    break;
                case "session.deleted.1":
                    HandleSessionDeletedSync(syncEvent);
                    break;
            }
        }

        private void HandleStreamEvent(StreamEvent streamEvent)
        {
            var type = streamEvent.EventType;
            var properties = streamEvent.Properties;
            var sessionID = streamEvent.SessionID;
            var directory = streamEvent.Directory;

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
                //    HandleSessionCreatedStream(properties);
                    break;

                case "session.updated":
                    HandleSessionUpdatedStream(properties);
                    break;

                case "session.deleted":
                //    HandleSessionDeletedStream(properties);
                    break;

                case "message.updated":
                    HandleMessageUpdatedStream(properties);
                    break;

                case "message.removed":
                //    HandleMessageRemovedStream(properties);
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

        private void HandleMessageUpdatedSync(MessageUpdatedSyncEvent evt)
        {
            var data = (KiloVisualStudioExtension.ApiClient.EventMessageUpdated)evt.Data;
            var info = data.Properties.Info;
            var infoJson = info.ToJson();
            var infoObj = JObject.Parse(infoJson);
            var messageID = infoObj["id"]?.Value<string>();
            var sessionID = infoObj["sessionID"]?.Value<string>();

            if (infoObj["cost"]?.Type == JTokenType.Float && infoObj["role"]?.Value<string>() == "assistant")
            {
                _messageCosts[messageID] = new MessageCost { SessionID = sessionID, MessageID = messageID, Cost = infoObj["cost"].Value<double>() };
            }

            var timeObj = infoObj["time"];
            var createdAt = timeObj != null && timeObj["created"]?.Type == JTokenType.Integer
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)timeObj["created"].Value<long>()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            var message = new WebViewMessage
            {
                Id = messageID,
                SessionID = sessionID,
                Role = infoObj["role"]?.Value<string>(),
                Content = infoObj["content"]?.ToString(),
                Parts = infoObj["parts"],
                CreatedAt = createdAt,
                Time = timeObj != null ? new { created = timeObj["created"]?.Value<long>(), updated = timeObj["updated"]?.Value<long>() } : null,
                Agent = infoObj["agent"]?.ToString(),
                Model = infoObj["model"]?.ToString(),
                ProviderID = infoObj["providerID"]?.Value<string>(),
                ModelID = infoObj["modelID"]?.Value<string>()
            };
            
            PostMessage(new MessageCreatedMessage { Message = message });
        }

        private void HandleMessageRemovedSync(SyncEvent evt)
        {
            var data = (KiloVisualStudioExtension.ApiClient.EventMessageRemoved)evt.Data;
            
            _messageCosts.Remove(data.Properties.MessageID);

            PostMessage(new MessageRemovedMessage
            {
                SessionID = data.Properties.SessionID,
                MessageID = data.Properties.MessageID
            });
        }

        private void HandlePartUpdatedSync(MessagePartUpdatedSyncEvent evt)
        {
            var data = (KiloVisualStudioExtension.ApiClient.EventMessagePartUpdated)evt.Data;
            var part = data.Properties.Part;
            var sessionID = data.Properties.SessionID;
            var partJson = part.ToJson();
            var partObj = JObject.Parse(partJson);
            var messageID = partObj["messageID"]?.Value<string>();
            
            var metadata = partObj["metadata"];
            if (metadata != null && metadata is JObject metadataObj && metadataObj["sessionId"] != null)
            {
                var childId = metadataObj["sessionId"].Value<string>();
                if (!string.IsNullOrEmpty(childId) && !_trackedSessionIds.Contains(childId))
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Auto-adopting child session: {childId}");
                    _trackedSessionIds.Add(childId);
                }
            }

            PostMessage(new PartUpdate
            {
                SessionID = sessionID,
                MessageID = messageID,
                Part = new { id = partObj["id"], type = partObj["type"], messageID = partObj["messageID"], text = partObj["text"] }
            });
        }

        private void HandlePartRemovedSync(SyncEvent evt)
        {
            var data = (KiloVisualStudioExtension.ApiClient.EventMessagePartRemoved)evt.Data;
            
            PostMessage(new PartRemove
            {
                SessionID = data.Properties.SessionID,
                MessageID = data.Properties.MessageID,
                PartID = data.Properties.PartID
            });
        }

        private void HandleSessionCreatedSync(SessionCreatedSyncEvent evt)
        {
            var data = (KiloVisualStudioExtension.ApiClient.EventSessionCreated)evt.Data;
            var info = data.Properties.Info;
            var sessionID = info.Id;
            
            if (string.IsNullOrEmpty(CurrentSessionID))
            {
                CurrentSessionID = sessionID;
                _trackedSessionIds.Add(sessionID);
            }

            var createdAt = info.Time != null
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Created).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            var updatedAt = info.Time != null
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Updated).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            PostMessage(new SessionCreatedMessage
            {
                Session = new SessionInfo
                {
                    Id = sessionID,
                    ParentID = info.ParentID,
                    Title = info.Title,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    Revert = info.Revert,
                    Summary = info.Summary
                }
            });
        }

        private void HandleSessionUpdatedSync(SessionUpdatedSyncEvent evt)
        {
            var data = (KiloVisualStudioExtension.ApiClient.EventSessionUpdated)evt.Data;
            var info = data.Properties.Info;
            var sessionID = data.Properties.SessionID;
            
            if (!string.IsNullOrEmpty(evt.Id))
            {
                var revision = new SessionRevision { Id = long.Parse(evt.Id), Seq = evt.Seq };
                _revisions[sessionID] = revision;
            }

            if (CurrentSessionID == sessionID)
            {
                CurrentSessionID = sessionID;
            }

            var createdAt = info.Time != null
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Created).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            var updatedAt = info.Time != null
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)info.Time.Updated).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            PostMessage(new SessionUpdatedMessage
            {
                Session = new SessionUpdate
                {
                    Id = sessionID,
                    ParentID = info.ParentID,
                    Title = info.Title,
                    CreatedAt = createdAt,
                    UpdatedAt = updatedAt,
                    Revert = info.Revert,
                    Summary = info.Summary
                }
            });
        }

        private void HandleSessionDeletedSync(SyncEvent evt)
        {
            var data = (KiloVisualStudioExtension.ApiClient.EventSessionDeleted)evt.Data;
            var sessionID = data.Properties.SessionID;
            
            if (!string.IsNullOrEmpty(sessionID))
            {
                _trackedSessionIds.Remove(sessionID);
                _modelUsageSessionIds.Remove(sessionID);
                _revisions.Remove(sessionID);
                _sessionStatusMap.Remove(sessionID);
                
                var costsToRemove = _messageCosts.Where(kvp => kvp.Value.SessionID == sessionID).Select(kvp => kvp.Key).ToArray();
                foreach (var costId in costsToRemove)
                {
                    _messageCosts.Remove(costId);
                }
            }

            PostMessage(new SessionDeletedMessage { SessionID = sessionID });
        }

        private void HandleMemoryEvent(string type, JToken properties)
        {
            var eventSessionID = properties["sessionID"]?.Value<string>();
            var active = CurrentSessionID;
            
            var local = string.IsNullOrEmpty(eventSessionID) || eventSessionID == active || _trackedSessionIds.Contains(eventSessionID);
            if (!local) return;

            object? detail = null;
            if (properties["detail"]?.Type == JTokenType.Object)
            {
                detail = properties["detail"];
            }
            else if (type == "memory.error" && properties["reason"]?.Type == JTokenType.String)
            {
                detail = JsonConvert.DeserializeObject<object>(JsonConvert.SerializeObject(new { type = "error", message = properties["reason"].Value<string>(), reason = properties["reason"].Value<string>() }));
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

        private void HandleSessionStatus(JToken properties, string? sessionID)
        {
            var status = properties["status"] ?? throw new JsonSerializationException("session.status missing 'status'");
            var statusType = status["type"]?.Value<string>();
            var sid = properties["sessionID"]?.Value<string>() ?? "";
            
            var prev = _sessionStatusMap.ContainsKey(sid) ? _sessionStatusMap[sid] : null;
            if ((prev == null || prev.Type == "idle") && statusType != "idle")
            {
                // costs.rearm(sid) - not implemented
            }
            
            _sessionStatusMap[sid] = new SessionStatus
            {
                Type = statusType ?? "",
                Attempt = status["attempt"]?.Value<int>() ?? 0,
                Message = status["message"]?.Value<string>(),
                Next = status["next"]?.Type == JTokenType.Float || status["next"]?.Type == JTokenType.Integer ? (long?)status["next"].Value<long>() : null
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

        private void HandlePartDelta(JToken properties)
        {
            var partID = properties["partID"]?.Value<string>();
            var messageID = properties["messageID"]?.Value<string>();
            var sid = properties["sessionID"]?.Value<string>();
            var delta = properties["delta"]?.Value<string>();
            
            System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: HandlePartDelta - sid={sid}, tracked={_trackedSessionIds.Contains(sid ?? "")}, deltaLen={delta?.Length ?? 0}");
            
            if (!string.IsNullOrEmpty(sid) && !_trackedSessionIds.Contains(sid))
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Skipping part update - session not tracked");
                return;
            }

            PostMessage(new
            {
                type = "partUpdated",
                sessionID = sid,
                messageID,
                part = new { id = partID, type = "text", messageID, text = delta },
                delta = new { type = "text-delta", textDelta = delta }
            });
        }

        private void HandleSessionCreatedStream(JToken properties)
        {
            var info = properties["info"] ?? throw new JsonSerializationException("session.created missing 'info'");
            var sessionID = info["id"]?.Value<string>() ?? "";
            
            if (string.IsNullOrEmpty(CurrentSessionID))
            {
                CurrentSessionID = sessionID;
                _trackedSessionIds.Add(sessionID);
            }

            var createdAt = info["time"]?["created"] != null
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)info["time"]["created"].Value<double>()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            var updatedAt = info["time"]?["updated"] != null
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)info["time"]["updated"].Value<double>()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            PostMessage(new
            {
                type = "sessionCreated",
                session = new
                {
                    id = sessionID,
                    parentID = info["parentID"]?.Type == JTokenType.String ? info["parentID"].Value<string>() : null,
                    title = info["title"]?.Value<string>(),
                    createdAt,
                    updatedAt,
                    revert = info["revert"]?.Type == JTokenType.Object ? info["revert"] : (object?)null,
                    summary = info["summary"]?.Type == JTokenType.String ? info["summary"].Value<string>() : null
                }
            });
        }

        private void HandleSessionUpdatedStream(JToken properties)
        {
            var sessionID = properties["sessionID"]?.Value<string>();
            var info = properties["info"] ?? throw new JsonSerializationException("session.updated missing 'info'");
            
            if (info["cost"] != null && info["cost"].Type == JTokenType.Float)
            {
                // requestCostAlert - not implemented
            }

            if (CurrentSessionID == sessionID)
            {
                CurrentSessionID = sessionID;
            }

            var createdAt = info["time"]?["created"] != null
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)info["time"]["created"].Value<double>()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            var updatedAt = info["time"]?["updated"] != null
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)info["time"]["updated"].Value<double>()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            PostMessage(new
            {
                type = "sessionUpdated",
                session = new
                {
                    id = sessionID,
                    parentID = info["parentID"]?.Type == JTokenType.String ? info["parentID"].Value<string>() : null,
                    title = info["title"]?.Value<string>(),
                    createdAt,
                    updatedAt,
                    revert = info["revert"]?.Type == JTokenType.Object ? info["revert"] : (object?)null,
                    summary = info["summary"]?.Type == JTokenType.String ? info["summary"].Value<string>() : null
                }
            });
        }

        private void HandleSessionDeletedStream(JToken properties)
        {
            var sessionID = properties["sessionID"]?.Value<string>();
            
            if (!string.IsNullOrEmpty(sessionID))
            {
                _trackedSessionIds.Remove(sessionID);
                _modelUsageSessionIds.Remove(sessionID);
                _revisions.Remove(sessionID);
                _sessionStatusMap.Remove(sessionID);
                
                var costsToRemove = _messageCosts.Where(kvp => kvp.Value.SessionID == sessionID).Select(kvp => kvp.Key).ToArray();
                foreach (var costId in costsToRemove)
                {
                    _messageCosts.Remove(costId);
                }
            }

            PostMessage(new
            {
                type = "sessionDeleted",
                sessionID
            });
        }

        private void HandleMessageUpdatedStream(JToken properties)
        {
            var info = properties["info"] ?? throw new JsonSerializationException("message.updated missing 'info'");
            var sessionID = info["sessionID"]?.Value<string>();
            var messageID = info["id"]?.Value<string>() ?? "";
            
            if (info["cost"] != null && info["cost"].Type == JTokenType.Float)
            {
                var cost = info["cost"].Value<double>();
                if (info["role"]?.Value<string>() == "assistant")
                {
                    _messageCosts[messageID] = new MessageCost { SessionID = sessionID ?? "", MessageID = messageID, Cost = cost };
                }
            }

            var createdAt = info["time"]?["created"] != null
                ? DateTimeOffset.FromUnixTimeMilliseconds((long)info["time"]["created"].Value<long>()).ToUniversalTime().ToString("o")
                : DateTime.UtcNow.ToString("o");
            
            // Match TypeScript: { ...info, createdAt: new Date(info.time.created).toISOString() }
            var messageObj = new Dictionary<string, object?>();
            if (info is JObject infoObj)
            {
                foreach (var prop in infoObj)
                {
                    messageObj[prop.Key] = prop.Value;
                }
            }
            messageObj["createdAt"] = createdAt;
            
            PostMessage(new
            {
                type = "messageCreated",
                message = messageObj
            });
        }

        private void HandleMessageRemovedStream(JToken properties)
        {
            var messageID = properties["messageID"]?.Value<string>();
            _messageCosts.Remove(messageID ?? "");

            PostMessage(new
            {
                type = "messageRemoved",
                sessionID = properties["sessionID"]?.Value<string>(),
                messageID
            });
        }

        private void HandleGlobalDisposed()
        {
            PostMessage(new { type = "global.disposed" });
        }

        private void HandleServerInstanceDisposed(JToken properties)
        {
            var dir = properties["directory"]?.Value<string>();
            
            foreach (var sid in _sessionStatusMap.Keys.ToList())
            {
                _sessionStatusMap[sid] = new SessionStatus { Type = "idle" };
            }

            PostMessage(new { type = "server.instance.disposed", directory = dir });
        }

        private void HandleGlobalConfigUpdated()
        {
            PostMessage(new { type = "globalConfigUpdated" });
        }

        private void HandlePartUpdatedStream(JToken properties)
        {
            var part = properties["part"] ?? throw new JsonSerializationException("message.part.updated missing 'part'");
            var sessionID = properties["sessionID"]?.Value<string>();
            var messageID = part["messageID"]?.Value<string>();
            
            if (part["metadata"]?["sessionId"] != null)
            {
                var childId = part["metadata"]["sessionId"].Value<string>();
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

        private void HandleIndexingStatus(JToken properties)
        {
            var status = properties["status"];
            _cachedIndexingStatusMessage = JsonConvert.SerializeObject(new { type = "indexingStatusLoaded", status });
            
            if (!string.IsNullOrEmpty(_cachedIndexingStatusMessage))
            {
                _postMessage(_cachedIndexingStatusMessage);
            }
        }

        private void HandleSessionTurnClosed(JToken properties)
        {
            PostMessage(new
            {
                type = "sessionTurnClosed",
                sessionID = properties["sessionID"]?.Value<string>(),
                reason = properties["reason"]?.Value<string>()
            });
        }

        private void HandlePermissionAsked(JToken properties)
        {
            var permission = properties["permission"]?.Value<string>();
            PostMessage(new
            {
                type = "permissionRequest",
                permission = new
                {
                    id = properties["id"]?.Value<string>(),
                    sessionID = properties["sessionID"]?.Value<string>(),
                    toolName = permission,
                    patterns = properties["patterns"] ?? JArray.Parse("[]"),
                    always = properties["always"] ?? JArray.Parse("[]"),
                    args = properties["metadata"] ?? JObject.Parse("{}"),
                    message = $"Permission required: {permission}",
                    tool = properties["tool"] ?? JObject.Parse("{}")
                }
            });
        }

        private void HandlePermissionReplied(JToken properties)
        {
            PostMessage(new
            {
                type = "permissionResolved",
                permissionID = properties["requestID"]?.Value<string>()
            });
        }

        private void HandleTodoUpdated(JToken properties)
        {
            PostMessage(new
            {
                type = "todoUpdated",
                sessionID = properties["sessionID"]?.Value<string>(),
                items = properties["todos"]
            });
        }

        private void HandleQuestionAsked(JToken properties)
        {
            PostMessage(new
            {
                type = "questionRequest",
                question = new
                {
                    id = properties["id"]?.Value<string>(),
                    sessionID = properties["sessionID"]?.Value<string>(),
                    questions = properties["questions"],
                    blocking = properties["blocking"]?.Value<bool>() ?? false,
                    tool = properties["tool"] ?? JObject.Parse("{}")
                }
            });
        }

        private void HandleQuestionResolved(JToken properties)
        {
            PostMessage(new
            {
                type = "questionResolved",
                requestID = properties["requestID"]?.Value<string>()
            });
        }

        private void HandleSuggestionShown(JToken properties)
        {
            PostMessage(new
            {
                type = "suggestionRequest",
                suggestion = new
                {
                    id = properties["id"]?.Value<string>(),
                    sessionID = properties["sessionID"]?.Value<string>(),
                    text = properties["text"]?.Value<string>(),
                    actions = properties["actions"],
                    blocking = properties["blocking"]?.Value<bool>() ?? false,
                    tool = properties["tool"] ?? JObject.Parse("{}")
                }
            });
        }

        private void HandleSuggestionResolved(JToken properties)
        {
            PostMessage(new
            {
                type = "suggestionResolved",
                requestID = properties["requestID"]?.Value<string>()
            });
        }

        private void HandleSessionError(JToken properties)
        {
            PostMessage(new
            {
                type = "sessionError",
                sessionID = properties["sessionID"]?.Value<string>(),
                error = properties["error"] ?? JObject.Parse("{}")
            });
        }

        private void HandleSandboxStatusChanged(JToken properties)
        {
            _sandboxRevision++;
            PostMessage(new
            {
                type = "sandboxStatus",
                sessionID = properties["sessionID"]?.Value<string>(),
                directory = properties["directory"]?.Value<string>(),
                enabled = properties["enabled"]?.Value<bool>() ?? false,
                available = properties["available"]?.Value<bool>() ?? false,
                reason = properties["reason"]?.Value<string>(),
                version = properties["version"]?.Value<int>() ?? 0,
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

        public void SetCurrentSession(string? sessionID)
        {
            CurrentSessionID = sessionID;
            if (!string.IsNullOrEmpty(sessionID))
            {
                _trackedSessionIds.Add(sessionID);
            }
        }
    }
}
