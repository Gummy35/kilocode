using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using KiloVisualStudioExtension.ApiClient.Json;
using KiloVisualStudioExtension.ApiClient.Sse;
using KiloVisualStudioExtension.WebView.Generated;
using KiloVisualStudioExtension.WebView.Generated.Sessions;
using KiloVisualStudioExtension.WebView.Generated.ExtensionMessages;
using KiloVisualStudioExtension.WebView.Generated.Parts;
using ApiMessage = KiloVisualStudioExtension.ApiClient.Message;
using WebViewMessage = KiloVisualStudioExtension.WebView.Generated.Sessions.Message;

namespace KiloVisualStudioExtension
{
    public class SSEHelper
    {
        private readonly HashSet<string> _trackedSessionIds = new HashSet<string>();
        private readonly Dictionary<string, SessionStatus> _sessionStatusMap = new Dictionary<string, SessionStatus>();
        private readonly Dictionary<string, SessionRevision> _revisions = new Dictionary<string, SessionRevision>();
        private readonly HashSet<string> _modelUsageSessionIds = new HashSet<string>();
        private readonly Dictionary<string, MessageCost> _messageCosts = new Dictionary<string, MessageCost>();
        private readonly Dictionary<string, string> _messageSessionIds = new Dictionary<string, string>();
        private readonly Dictionary<string, string> _networkWaits = new Dictionary<string, string>();
        private int _sandboxRevision = 0;
        private string? _cachedIndexingStatusMessage = null;
        private string? _currentProjectID = null;

        public string? CurrentSessionID { get; private set; }
        public string? CurrentProjectID 
        { 
            get => _currentProjectID;
            set => _currentProjectID = value;
        }

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

        public void HandleEvent(SseEventReceivedEventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"SSE Event received : {e.EventType}");

                var sseEvent = SseEventDeserializer.Deserialize(e);

                //if (sseEvent is SyncEvent syncEvent)
                //{
                //    HandleSyncEvent(syncEvent);
                //}
                //else if (sseEvent is StreamEvent streamEvent)
                //{
                //    HandleStreamEvent(streamEvent);
                //}
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

                case "session.turn.open":
                    HandleSessionTurnOpen(properties);
                    break;

                case "session.network.asked":
                case "session.network.replied":
                case "session.network.rejected":
                case "session.network.restored":
                    HandleNetworkEvent(type, properties);
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
            var data = (ApiClient.EventMessageUpdated)evt.Data;
            var info = data.Properties.Info;
            var infoJson = info.ToJson();
            var infoObj = JObject.Parse(infoJson);
            var messageID = infoObj["id"]?.Value<string>();
            var sessionID = data.Properties.SessionID;

            RecordMessageSessionId(messageID, sessionID);

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
            var data = (ApiClient.EventSessionUpdated)evt.Data;
            var info = data.Properties.Info;
            var sessionID = data.Properties.SessionID;
            
            if (!string.IsNullOrEmpty(evt.Id) && evt.Seq > 0)
            {
                if (IsStaleEvent(sessionID, evt.Id, evt.Seq))
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Dropping stale session.updated event for {sessionID}");
                    return;
                }
                UpdateRevision(sessionID, evt.Id, evt.Seq);
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

            var sessionStatus = new WebView.Generated.ExtensionMessages.SessionStatusMessage
            {
                SessionID = sid,
                Status = MapSessionStatusEnum(statusType),
                Attempt = status["attempt"]?.Type == JTokenType.Integer || status["attempt"]?.Type == JTokenType.Float ? (double?)status["attempt"].Value<long>() : null,
                Message = status["message"]?.Value<string>(),
                Next = status["next"]?.Type == JTokenType.Float || status["next"]?.Type == JTokenType.Integer ? (double?)status["next"].Value<long>() : null
            };

            PostMessage(sessionStatus);
        }

        private WebView.Generated.Connection.SessionStatus MapSessionStatusEnum(string? type)
        {
            return type switch
            {
                "idle" => WebView.Generated.Connection.SessionStatus.Idle,
                "busy" => WebView.Generated.Connection.SessionStatus.Busy,
                "retry" => WebView.Generated.Connection.SessionStatus.Retry,
                "offline" => WebView.Generated.Connection.SessionStatus.Offline,
                _ => WebView.Generated.Connection.SessionStatus.Idle
            };
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

            PostMessage(new WebView.Generated.PartUpdate
            {
                SessionID = sid,
                MessageID = messageID,
                Part = new { id = partID, type = "text", messageID, text = delta },
                Delta = new { type = "text-delta", textDelta = delta }
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

        private void HandleSessionTurnOpen(JToken properties)
        {
            var sessionID = properties["sessionID"]?.Value<string>();
            System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: session.turn.open for {sessionID}");
        }

        private void HandleNetworkEvent(string type, JToken properties)
        {
            var requestID = properties["requestID"]?.Value<string>() ?? properties["id"]?.Value<string>();
            var sessionID = properties["sessionID"]?.Value<string>();
            
            System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: network event {type} for session {sessionID}, requestID {requestID}");
            
            if (type == "session.network.asked" && !string.IsNullOrEmpty(requestID))
            {
                _networkWaits[requestID] = sessionID ?? "";
            }
            else if (type == "session.network.restored" && !string.IsNullOrEmpty(requestID))
            {
                if (_networkWaits.TryGetValue(requestID, out var sid))
                {
                    System.Diagnostics.Debug.WriteLine($"[Kilo] SSEHelper: Auto-replying to network restore for {requestID}");
                    _networkWaits.Remove(requestID);
                }
            }
            else if (type == "session.network.replied" || type == "session.network.rejected")
            {
                _networkWaits.Remove(requestID ?? "");
            }
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

        public string? ResolveSessionId(SseEvent evt)
        {
            if (evt is SyncEvent syncEvent)
            {
                return ResolveSyncSessionId(syncEvent);
            }

            if (evt is StreamEvent streamEvent)
            {
                return ResolveTransientSessionId(streamEvent);
            }

            return null;
        }

        private string? ResolveSyncSessionId(SyncEvent evt)
        {
            if (evt.Name == "message.updated.1" && evt.Data is ApiClient.EventMessageUpdated msgUpdated)
            {
                var infoJson = msgUpdated.Properties.Info.ToJson();
                var infoObj = JObject.Parse(infoJson);
                var messageID = infoObj["id"]?.Value<string>();
                var sessionID = msgUpdated.Properties.SessionID;
                if (!string.IsNullOrEmpty(messageID) && !string.IsNullOrEmpty(sessionID))
                {
                    _messageSessionIds[messageID] = sessionID;
                }
                return sessionID;
            }

            if (evt.Name == "message.removed.1" && evt.Data is ApiClient.EventMessageRemoved msgRemoved)
            {
                return msgRemoved.Properties.SessionID;
            }

            if (evt.Name == "message.part.updated.1" && evt.Data is ApiClient.EventMessagePartUpdated partUpdated)
            {
                return partUpdated.Properties.SessionID;
            }

            if (evt.Name == "message.part.removed.1" && evt.Data is ApiClient.EventMessagePartRemoved partRemoved)
            {
                return partRemoved.Properties.SessionID;
            }

            if (evt.Name == "session.created.1" && evt.Data is ApiClient.EventSessionCreated sessionCreated)
            {
                return sessionCreated.Properties.SessionID;
            }

            if (evt.Name == "session.updated.1" && evt.Data is ApiClient.EventSessionUpdated sessionUpdated)
            {
                return sessionUpdated.Properties.SessionID;
            }

            if (evt.Name == "session.deleted.1" && evt.Data is ApiClient.EventSessionDeleted sessionDeleted)
            {
                return sessionDeleted.Properties.SessionID;
            }

            return null;
        }

        private string? ResolveTransientSessionId(StreamEvent evt)
        {
            var type = evt.EventType;
            switch (type)
            {
                case "session.status":
                case "session.turn.open":
                case "session.turn.close":
                case "session.idle":
                case "session.error":
                case "todo.updated":
                case "message.part.delta":
                case "message.part.updated":
                case "permission.asked":
                case "permission.replied":
                case "question.asked":
                case "question.replied":
                case "question.rejected":
                case "suggestion.shown":
                case "suggestion.accepted":
                case "suggestion.dismissed":
                case "session.network.asked":
                case "session.network.replied":
                case "session.network.rejected":
                case "session.network.restored":
                    return evt.SessionID;
                case "sandbox.status.changed":
                    return evt.SessionID;
                default:
                    return null;
            }
        }

        public void RecordMessageSessionId(string messageID, string sessionID)
        {
            _messageSessionIds[messageID] = sessionID;
        }

        public string? LookupMessageSessionId(string messageID)
        {
            return _messageSessionIds.TryGetValue(messageID, out var sessionId) ? sessionId : null;
        }

        public bool IsStaleEvent(string sessionID, string eventId, int seq)
        {
            if (!_revisions.TryGetValue(sessionID, out var revision))
            {
                return false;
            }

            var versioned = seq > 0 || revision.Seq > 0;
            if (versioned)
            {
                return seq <= revision.Seq;
            }

            return long.Parse(eventId) <= revision.Id;
        }

        public void UpdateRevision(string sessionID, string eventId, int seq)
        {
            _revisions[sessionID] = new SessionRevision 
            { 
                Id = long.Parse(eventId), 
                Seq = seq 
            };
        }

        public bool IsEventFromForeignProject(string eventName, string? projectID)
        {
            if (string.IsNullOrEmpty(_currentProjectID) || string.IsNullOrEmpty(projectID))
            {
                return false;
            }

            if (eventName == "session.created.1" || eventName == "session.deleted.1")
            {
                return projectID != _currentProjectID;
            }

            if (eventName == "session.updated.1")
            {
                return projectID != _currentProjectID;
            }

            return false;
        }

        public void SetProjectID(string? projectID)
        {
            _currentProjectID = projectID;
        }
    }
}
