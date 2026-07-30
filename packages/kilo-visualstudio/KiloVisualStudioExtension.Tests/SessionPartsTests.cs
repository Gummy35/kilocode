using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class SessionPartsTests
    {
        private class Part
        {
            public string Id { get; set; } = "";
            public string Type { get; set; } = "";
            public string Text { get; set; } = "";
        }

        private class Message
        {
            public string Id { get; set; } = "";
            public string SessionID { get; set; } = "";
            public string Role { get; set; } = "";
            public List<Part> Parts { get; set; } = new();
        }

        private class PartUpdate
        {
            public string SessionID { get; set; } = "";
            public string MessageID { get; set; } = "";
            public string PartID { get; set; } = "";
            public string Text { get; set; } = "";
            public string UpdateType { get; set; } = "";
        }

        private class SessionPartsManager
        {
            public Dictionary<string, Message> Messages { get; } = new();
            public List<Dictionary<string, object>> PostedMessages { get; } = new();

            public void AddMessage(Message msg)
            {
                Messages[msg.Id] = msg;
            }

            public void ApplyPartUpdate(PartUpdate update)
            {
                if (!Messages.TryGetValue(update.MessageID, out var msg))
                    return;

                var part = msg.Parts.FirstOrDefault(p => p.Id == update.PartID);
                if (part == null)
                {
                    part = new Part { Id = update.PartID, Type = "reasoning", Text = "" };
                    msg.Parts.Add(part);
                }

                if (update.UpdateType == "append")
                {
                    part.Text += update.Text;
                }
                else if (update.UpdateType == "replace")
                {
                    part.Text = update.Text;
                }

                PostedMessages.Add(new Dictionary<string, object>
                {
                    ["type"] = "partUpdated",
                    ["sessionID"] = update.SessionID,
                    ["messageID"] = update.MessageID,
                    ["partID"] = update.PartID,
                    ["text"] = part.Text
                });
            }

            public void CoalesceUpdates(string messageID, List<PartUpdate> pendingUpdates)
            {
                var updatesByPart = pendingUpdates.GroupBy(u => u.PartID)
                    .ToDictionary(g => g.Key, g => g.Last());

                foreach (var update in updatesByPart.Values)
                {
                    ApplyPartUpdate(update);
                }
            }
        }

        [Fact]
        public void ApplyPartUpdate_AppendsTextToExistingPart()
        {
            var manager = new SessionPartsManager();
            var msg = new Message
            {
                Id = "m1",
                SessionID = "s1",
                Role = "assistant",
                Parts = new List<Part>
                {
                    new Part { Id = "r1", Type = "reasoning", Text = "Initial " }
                }
            };
            manager.AddMessage(msg);

            manager.ApplyPartUpdate(new PartUpdate
            {
                SessionID = "s1",
                MessageID = "m1",
                PartID = "r1",
                Text = "reasoning",
                UpdateType = "append"
            });

            Assert.Equal("Initial reasoning", msg.Parts[0].Text);
            Assert.Single(manager.PostedMessages);
        }

        [Fact]
        public void ApplyPartUpdate_ReplacesTextInPart()
        {
            var manager = new SessionPartsManager();
            var msg = new Message
            {
                Id = "m1",
                SessionID = "s1",
                Role = "assistant",
                Parts = new List<Part>
                {
                    new Part { Id = "r1", Type = "reasoning", Text = "Old text" }
                }
            };
            manager.AddMessage(msg);

            manager.ApplyPartUpdate(new PartUpdate
            {
                SessionID = "s1",
                MessageID = "m1",
                PartID = "r1",
                Text = "New text",
                UpdateType = "replace"
            });

            Assert.Equal("New text", msg.Parts[0].Text);
        }

        [Fact]
        public void ApplyPartUpdate_CreatesPartIfNotExists()
        {
            var manager = new SessionPartsManager();
            var msg = new Message
            {
                Id = "m1",
                SessionID = "s1",
                Role = "assistant",
                Parts = new List<Part>()
            };
            manager.AddMessage(msg);

            manager.ApplyPartUpdate(new PartUpdate
            {
                SessionID = "s1",
                MessageID = "m1",
                PartID = "r1",
                Text = "New part",
                UpdateType = "append"
            });

            Assert.Single(msg.Parts);
            Assert.Equal("r1", msg.Parts[0].Id);
            Assert.Equal("New part", msg.Parts[0].Text);
        }

        [Fact]
        public void ApplyPartUpdate_IgnoresUnknownMessage()
        {
            var manager = new SessionPartsManager();

            manager.ApplyPartUpdate(new PartUpdate
            {
                SessionID = "s1",
                MessageID = "unknown",
                PartID = "r1",
                Text = "Text",
                UpdateType = "append"
            });

            Assert.Empty(manager.PostedMessages);
        }

        [Fact]
        public void CoalesceUpdates_OnlyAppliesLastUpdatePerPart()
        {
            var manager = new SessionPartsManager();
            var msg = new Message
            {
                Id = "m1",
                SessionID = "s1",
                Role = "assistant",
                Parts = new List<Part>
                {
                    new Part { Id = "r1", Type = "reasoning", Text = "" }
                }
            };
            manager.AddMessage(msg);

            var updates = new List<PartUpdate>
            {
                new PartUpdate { SessionID = "s1", MessageID = "m1", PartID = "r1", Text = "First", UpdateType = "append" },
                new PartUpdate { SessionID = "s1", MessageID = "m1", PartID = "r1", Text = "Second", UpdateType = "append" },
                new PartUpdate { SessionID = "s1", MessageID = "m1", PartID = "r1", Text = "Third", UpdateType = "append" }
            };

            manager.CoalesceUpdates("m1", updates);

            Assert.Equal("Third", msg.Parts[0].Text);
            Assert.Single(manager.PostedMessages);
        }

        [Fact]
        public void CoalesceUpdates_AppliesUpdatesForMultipleParts()
        {
            var manager = new SessionPartsManager();
            var msg = new Message
            {
                Id = "m1",
                SessionID = "s1",
                Role = "assistant",
                Parts = new List<Part>
                {
                    new Part { Id = "r1", Type = "reasoning", Text = "" },
                    new Part { Id = "c1", Type = "text", Text = "" }
                }
            };
            manager.AddMessage(msg);

            var updates = new List<PartUpdate>
            {
                new PartUpdate { SessionID = "s1", MessageID = "m1", PartID = "r1", Text = "Reasoning", UpdateType = "append" },
                new PartUpdate { SessionID = "s1", MessageID = "m1", PartID = "c1", Text = "Content", UpdateType = "append" },
                new PartUpdate { SessionID = "s1", MessageID = "m1", PartID = "r1", Text = " more", UpdateType = "append" }
            };

            manager.CoalesceUpdates("m1", updates);

            Assert.Equal("Reasoning more", msg.Parts[0].Text);
            Assert.Equal("Content", msg.Parts[1].Text);
            Assert.Equal(2, manager.PostedMessages.Count);
        }
    }
}
