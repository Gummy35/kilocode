using System;
using System.Collections.Generic;
using System.Linq;

namespace KiloVisualStudioExtension.Services
{
    public class SessionUtils
    {
        public string ComputeStatus(SessionUtilsPart part, Func<string, string> translate)
        {
            if (part == null)
                return null;

            if (part.Type == "tool")
            {
                switch (part.Tool)
                {
                    case "task":
                        return translate("ui.sessionTurn.status.delegating");
                    case "todowrite":
                    case "todoread":
                        return translate("ui.sessionTurn.status.planning");
                    case "read":
                        return translate("ui.sessionTurn.status.gatheringContext");
                    case "list":
                    case "grep":
                    case "glob":
                        return translate("ui.sessionTurn.status.searchingCodebase");
                    case "webfetch":
                        return translate("ui.sessionTurn.status.searchingWeb");
                    case "edit":
                    case "write":
                        return translate("ui.sessionTurn.status.makingEdits");
                    case "bash":
                        return translate("ui.sessionTurn.status.runningCommands");
                    default:
                        return null;
                }
            }

            if (part.Type == "reasoning")
                return translate("ui.sessionTurn.status.thinking");

            if (part.Type == "text")
            {
                if (part.Synthetic && !string.IsNullOrEmpty(part.Text))
                {
                    var syntheticMatch = System.Text.RegularExpressions.Regex.Match(part.Text, @"^(.+?)…$");
                    if (syntheticMatch.Success)
                        return syntheticMatch.Groups[1].Value + "...";
                }
                return translate("session.status.writingResponse");
            }

            return null;
        }

        public List<SessionInfo> RecentSessions(List<SessionInfo> sessions)
        {
            var parentIds = new HashSet<string>(sessions.Where(s => !string.IsNullOrEmpty(s.ParentID)).Select(s => s.ParentID));
            var rootSessions = sessions
                .Where(s => s.ParentID == null && !parentIds.Contains(s.Id))
                .OrderByDescending(s => s.UpdatedAt)
                .ToList();

            return rootSessions;
        }

        public double CalcTotalCost(List<Message> messages)
        {
            return messages
                .Where(m => m.Role == "assistant")
                .Sum(m => m.Cost ?? 0);
        }

        public (int tokens, int? percentage) CalcContextUsage(TokenUsage tokens, int? contextLimit)
        {
            var total = tokens.Input + tokens.Output + (tokens.Reasoning ?? 0) + (tokens.Cache?.Read ?? 0) + (tokens.Cache?.Write ?? 0);
            var percentage = contextLimit.HasValue ? (int)(total * 100.0 / contextLimit.Value) : (int?)null;
            return (total, percentage);
        }

        public (int input, int output, int cached) CalcTokenUsage(List<Message> messages)
        {
            int input = 0, output = 0, cached = 0;

            foreach (var msg in messages)
            {
                if (msg.Role != "assistant" || msg.Tokens == null)
                    continue;

                input += msg.Tokens.Input;
                output += msg.Tokens.Output;
                cached += msg.Tokens.Cache?.Read ?? 0;
            }

            if (input == 0 && output == 0 && cached == 0)
                return default;

            return (input, output, cached);
        }

        public string ChildID(SessionUtilsToolPart part)
        {
            if (part.Tool != "task")
                return null;

            if (part.Metadata?.SessionId != null)
                return part.Metadata.SessionId;

            return part.State?.Metadata?.SessionId;
        }

        public Dictionary<string, double> BuildFamilyCosts(HashSet<string> family, Dictionary<string, List<Message>> messages, Dictionary<string, SessionInfo> sessions, Dictionary<string, string> parents = null)
        {
            var costs = new Dictionary<string, double>();

            foreach (var sessionId in family)
            {
                var ownCost = CalcTotalCost(messages.TryGetValue(sessionId, out var msgs) ? msgs : new List<Message>());
                if (Math.Round(ownCost, 2) == 0)
                    continue;

                var session = sessions[sessionId];
                if (!string.IsNullOrEmpty(session.ParentID) && family.Contains(session.ParentID))
                {
                    var parentOwnCost = ownCost;
                    foreach (var childId in family.Where(s => sessions.TryGetValue(s, out var sInfo) && sInfo.ParentID == sessionId))
                    {
                        if (costs.TryGetValue(childId, out var childCost))
                            parentOwnCost -= childCost;
                    }
                    costs[sessionId] = Math.Max(0, parentOwnCost);
                }
                else
                {
                    costs[sessionId] = ownCost;
                }
            }

            return costs;
        }

        public Dictionary<string, string> BuildFamilyParents(HashSet<string> family, Dictionary<string, List<Message>> messages, Dictionary<string, List<SessionUtilsPart>> parts)
        {
            var parents = new Dictionary<string, string>();

            foreach (var sessionId in family)
            {
                if (messages.TryGetValue(sessionId, out var msgs))
                {
                    foreach (var msg in msgs)
                    {
                        if (parts.TryGetValue(msg.Id, out var msgParts))
                        {
                            foreach (var part in msgParts.OfType<SessionUtilsToolPart>())
                            {
                                if (part.Tool == "task" && part.State?.Metadata?.SessionId != null && family.Contains(part.State.Metadata.SessionId))
                                {
                                    parents[part.State.Metadata.SessionId] = sessionId;
                                }
                            }
                        }
                    }
                }
            }

            return parents;
        }

        public List<SessionUtilsToolPart> BuildSessionToolParts(List<Message> messages, Func<Message, List<SessionUtilsPart>> getParts = null)
        {
            var tools = new List<SessionUtilsToolPart>();

            foreach (var msg in messages.Where(m => m.Role == "assistant"))
            {
                List<SessionUtilsPart> msgParts;
                if (getParts != null)
                {
                    msgParts = getParts(msg);
                }
                else if (msg.Parts != null)
                {
                    msgParts = msg.Parts.Cast<SessionUtilsPart>().ToList();
                }
                else
                {
                    continue;
                }

                foreach (var part in msgParts.OfType<SessionUtilsToolPart>())
                {
                    part.MessageID = msg.Id;
                    tools.Add(part);
                }
            }

            return tools;
        }

        public List<SessionUtilsToolPart> UpsertSessionToolPart(List<SessionUtilsToolPart> indexed, SessionUtilsToolPart part, Message message)
        {
            var existingIndex = indexed.FindIndex(p => p.Id == part.Id);
            if (existingIndex >= 0)
            {
                indexed[existingIndex] = part;
            }
            else
            {
                part.MessageID = message.Id;
                indexed.Add(part);
            }

            return indexed.Where(p => p.Type != "text").ToList();
        }

        public List<SessionUtilsToolPart> RemoveSessionToolPart(List<SessionUtilsToolPart> indexed, string partId)
        {
            return indexed.Where(p => p.Id != partId).ToList();
        }

        public List<SessionUtilsToolPart> RemoveSessionToolPartsForMessage(List<SessionUtilsToolPart> indexed, string messageId)
        {
            return indexed.Where(p => p.MessageID != messageId).ToList();
        }

        public Dictionary<string, string> BuildFamilyLabels(HashSet<string> family, Dictionary<string, List<Message>> messages, Dictionary<string, List<SessionUtilsPart>> parts)
        {
            var labels = new Dictionary<string, string>();

            foreach (var sessionId in family)
            {
                if (messages.TryGetValue(sessionId, out var msgs))
                {
                    foreach (var msg in msgs)
                    {
                        if (parts.TryGetValue(msg.Id, out var msgParts))
                        {
                            foreach (var part in msgParts.OfType<SessionUtilsToolPart>())
                            {
                                if (part.Tool == "task")
                                {
                                    var childId = part.State?.Metadata?.SessionId ?? part.Metadata?.SessionId;
                                    if (childId != null && family.Contains(childId) && !labels.ContainsKey(childId))
                                    {
                                    var label = part.State?.Input != null && part.State.Input.TryGetValue("subagent_type", out var subagentType) ? subagentType?.ToString()
                                        : part.State?.Input != null && part.State.Input.TryGetValue("description", out var desc) ? desc?.ToString()
                                        : part.Tool;
                                        
                                        if (label.Length > 24)
                                            label = label.Substring(0, 24) + "…";
                                        
                                        labels[childId] = label;
                                    }
                                }
                            }
                        }
                    }
                }
            }

            return labels;
        }

        public List<(string label, double cost)> BuildCostBreakdown(string rootId, Dictionary<string, double> costs, Dictionary<string, string> labels, string rootLabel)
        {
            var result = new List<(string, double)>();

            if (costs.TryGetValue(rootId, out var rootCost))
                result.Add((rootLabel, rootCost));

            foreach (var kvp in costs)
            {
                if (kvp.Key == rootId)
                    continue;

                var label = labels.TryGetValue(kvp.Key, out var l) ? l : kvp.Key.Substring(0, Math.Min(8, kvp.Key.Length));
                result.Add((label, kvp.Value));
            }

            return result;
        }

        public List<(string label, double cost)> CollapseCostBreakdown(List<(string label, double cost)> items, Func<int, string> summary)
        {
            if (items.Count <= 1)
                return items;

            var root = items[0];
            var children = items.Skip(1).Reverse().ToList();

            if (children.Count <= 8)
                return new List<(string, double)> { root }.Concat(children).ToList();

            var visibleChildren = children.Take(8).ToList();
            var hiddenChildren = children.Skip(8).ToList();
            var hiddenCost = hiddenChildren.Sum(c => c.cost);

            var result = new List<(string, double)> { root };
            result.AddRange(visibleChildren);
            result.Add((summary(hiddenChildren.Count), hiddenCost));

            return result;
        }

        public (int generation, string source) LatestMetrics(List<SessionUtilsPart> parts)
        {
            SessionUtilsPart lastWithMetrics = null;
            foreach (var part in parts.Where(p => p.Type == "step-finish"))
            {
                if (part.Metrics != null && part.Metrics.Generation > 0)
                    lastWithMetrics = part;
            }

            if (lastWithMetrics == null)
                return default;

            return (lastWithMetrics.Metrics.Generation, lastWithMetrics.Metrics.Source);
        }

        public (int generation, string source) AggregateMetrics(List<SessionUtilsPart> parts)
        {
            return LatestMetrics(parts);
        }

        public (int generation, string source) MessageThroughput(List<SessionUtilsPart> parts)
        {
            int totalOutputTokens = 0;
            int totalElapsedMs = 0;

            foreach (var part in parts.Where(p => p.Type == "step-finish" && p.Time != null && p.Time.Elapsed > 0))
            {
                var output = part.Tokens?.Output ?? 0;
                var reasoning = part.Tokens?.Reasoning ?? 0;
                if (output + reasoning > 0)
                {
                    totalOutputTokens += output + reasoning;
                    totalElapsedMs += part.Time.Elapsed;
                }
            }

            if (totalElapsedMs == 0)
                return default;

            return ((int)(totalOutputTokens * 1000.0 / totalElapsedMs), "computed");
        }

        public (int generation, string source) SessionThroughput(List<SessionUtilsPart> parts)
        {
            return MessageThroughput(parts);
        }

        public string FormatTG(int? value, string locale)
        {
            if (!value.HasValue || value <= 0 || double.IsNaN(value.Value))
                return "–";

            return $"{value} t/s";
        }
    }

    public class SessionInfo
    {
        public string Id { get; set; }
        public string UpdatedAt { get; set; }
        public string ParentID { get; set; }
    }

    public class Message
    {
        public string Id { get; set; }
        public string Role { get; set; }
        public double? Cost { get; set; }
        public TokenUsage Tokens { get; set; }
        public List<Part> Parts { get; set; }
    }

    public class TokenUsage
    {
        public int Input { get; set; }
        public int Output { get; set; }
        public int? Reasoning { get; set; }
        public CacheUsage Cache { get; set; }
    }

    public class CacheUsage
    {
        public int Read { get; set; }
        public int Write { get; set; }
    }

    public class SessionUtilsPart
    {
        public string Id { get; set; }
        public string Type { get; set; }
        public string Tool { get; set; }
        public string Text { get; set; }
        public SessionUtilsPartState State { get; set; }
        public SessionUtilsPartMetadata Metadata { get; set; }
        public bool Synthetic { get; set; }
        public TokenUsage Tokens { get; set; }
        public TimeInfo Time { get; set; }
        public Metrics Metrics { get; set; }
        public string MessageID { get; set; }
    }

    public class SessionUtilsToolPart : SessionUtilsPart
    {
        public SessionUtilsToolPart()
        {
            Type = "tool";
        }
    }

    public class SessionUtilsPartState
    {
        public string Status { get; set; }
        public Dictionary<string, object> Input { get; set; }
        public SessionUtilsPartMetadata Metadata { get; set; }
    }

    public class SessionUtilsPartMetadata
    {
        public string SessionId { get; set; }
    }

    public class Metrics
    {
        public int Generation { get; set; }
        public string Source { get; set; }
    }

    public class TimeInfo
    {
        public int Start { get; set; }
        public int End { get; set; }
        public int Elapsed { get; set; }
    }
}
