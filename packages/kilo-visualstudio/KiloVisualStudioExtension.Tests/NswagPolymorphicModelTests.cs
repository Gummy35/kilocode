using System;
using System.Collections.Generic;
using System.Text;
using FluentAssertions;
using KiloVisualStudioExtension.ApiClient;
using Newtonsoft.Json;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for NSwag-generated polymorphic model deserialization.
    /// Validates that real-world JSON payloads from the Kilo CLI can be correctly
    /// deserialized and that semantic information is preserved.
    /// 
    /// This addresses the validation report concern about oneOf/anyOf schema handling.
    /// </summary>
    public class NswagPolymorphicModelTests
    {
        private readonly JsonSerializerSettings _settings = new JsonSerializerSettings
        {
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        #region Part Models

        [Fact]
        public void TextPart_Deserialization_PreservesFields()
        {
            // Arrange - Realistic payload from session text streaming
            var json = @"{
                ""type"": ""text"",
                ""text"": ""Hello, this is assistant response text"",
                ""delta"": ""Hello, this is assistant response text""
            }";

            // Act
            var part = JsonConvert.DeserializeObject<TextPart>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Type.Should().Be("text");
            part.Text.Should().Be("Hello, this is assistant response text");
            part.Delta.Should().Be("Hello, this is assistant response text");
        }

        [Fact]
        public void FilePart_Deserialization_PreservesFileSource()
        {
            // Arrange - Realistic file part payload
            var json = @"{
                ""type"": ""file"",
                ""file"": {
                    ""path"": ""C:\\proj\\src\\test.cs"",
                    ""range"": {
                        ""start"": { ""line"": 10, ""character"": 0 },
                        ""end"": { ""line"": 20, ""character"": 5 }
                    },
                    ""text"": ""public class Test { }"",
                    ""source"": ""text""
                }
            }";

            // Act
            var part = JsonConvert.DeserializeObject<FilePart>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Type.Should().Be("file");
            part.File.Should().NotBeNull();
            part.File.Path.Should().Be("C:\\proj\\src\\test.cs");
            part.File.Range.Should().NotBeNull();
            part.File.Range.Start.Line.Should().Be(10);
            part.File.Text.Should().Be("public class Test { }");
        }

        [Fact]
        public void ToolPart_Deserialization_PreservesToolState()
        {
            // Arrange - Realistic tool part payload
            var json = @"{
                ""type"": ""tool"",
                ""tool"": {
                    ""name"": ""read"",
                    ""input"": { ""path"": ""test.txt"" },
                    ""output"": ""file contents here"",
                    ""status"": ""success""
                }
            }";

            // Act
            var part = JsonConvert.DeserializeObject<ToolPart>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Type.Should().Be("tool");
            part.Tool.Should().NotBeNull();
            part.Tool.Name.Should().Be("read");
            part.Tool.Input.Should().NotBeNull();
            part.Tool.Output.Should().Be("file contents here");
        }

        [Fact]
        public void Part_AdditionalProperties_CapturesUnknownFields()
        {
            // Arrange - Payload with additional fields not in schema
            var json = @"{
                ""type"": ""text"",
                ""text"": ""test"",
                ""customField"": ""custom value"",
                ""nested"": { ""a"": 1, ""b"": 2 }
            }";

            // Act
            var part = JsonConvert.DeserializeObject<Part>(json, _settings);

            // Assert
            part.Should().NotBeNull();
            part.Type.Should().Be("text");
            // NSwag generates AdditionalProperties dictionary for unknown fields
            part.AdditionalProperties.Should().ContainKey("customField");
            part.AdditionalProperties["customField"].Should().Be("custom value");
            part.AdditionalProperties.Should().ContainKey("nested");
        }

        #endregion

        #region Message Models

        [Fact]
        public void AssistantMessage_Deserialization_PreservesParts()
        {
            // Arrange - Realistic assistant message
            var json = @"{
                ""role"": ""assistant"",
                ""parts"": [
                    { ""type"": ""text"", ""text"": ""Let me help you with that."" },
                    { ""type"": ""tool"", ""tool"": { ""name"": ""read"", ""input"": {} } }
                ],
                ""messageID"": ""msg-123"",
                ""requestID"": ""req-456""
            }";

            // Act
            var message = JsonConvert.DeserializeObject<AssistantMessage>(json, _settings);

            // Assert
            message.Should().NotBeNull();
            message.Role.Should().Be("assistant");
            message.Parts.Should().HaveCount(2);
            message.MessageID.Should().Be("msg-123");
            message.RequestID.Should().Be("req-456");
        }

        [Fact]
        public void Message_AdditionalProperties_HandlesExtraFields()
        {
            // Arrange
            var json = @"{
                ""role"": ""assistant"",
                ""parts"": [],
                ""metadata"": { ""custom"": ""value"" },
                ""timestamp"": 1234567890
            }";

            // Act
            var message = JsonConvert.DeserializeObject<Message>(json, _settings);

            // Assert
            message.Should().NotBeNull();
            message.Role.Should().Be("assistant");
            message.AdditionalProperties.Should().ContainKey("metadata");
            message.AdditionalProperties.Should().ContainKey("timestamp");
        }

        #endregion

        #region Event Models

        [Fact]
        public void EventTuiPromptAppend_Deserialization_PreservesText()
        {
            // Arrange - Realistic TUI prompt append event
            var json = @"{
                ""type"": ""tui.prompt.append"",
                ""text"": ""User prompt content here""
            }";

            // Act
            var evt = JsonConvert.DeserializeObject<EventTuiPromptAppend>(json, _settings);

            // Assert
            evt.Should().NotBeNull();
            evt.Type.Should().Be(EventTuiPromptAppend.TypeValue.TuiPromptAppend);
            evt.Text.Should().Be("User prompt content here");
        }

        [Fact]
        public void EventTuiToastShow_Deserialization_PreservesAllFields()
        {
            // Arrange - Realistic toast event
            var json = @"{
                ""type"": ""tui.toast.show"",
                ""title"": ""Warning"",
                ""message"": ""This action cannot be undone"",
                ""variant"": ""warning"",
                ""duration"": 5000
            }";

            // Act
            var evt = JsonConvert.DeserializeObject<EventTuiToastShow>(json, _settings);

            // Assert
            evt.Should().NotBeNull();
            evt.Type.Should().Be(EventTuiToastShow.TypeValue.TuiToastShow);
            evt.Title.Should().Be("Warning");
            evt.Message.Should().Be("This action cannot be undone");
            evt.Variant.Should().Be("warning");
            evt.Duration.Should().Be(5000);
        }

        [Fact]
        public void EventSessionCreated_Deserialization_PreservesSessionInfo()
        {
            // Arrange - Realistic session created event
            var json = @"{
                ""type"": ""session.created"",
                ""session"": {
                    ""id"": ""sess-abc123"",
                    ""title"": ""My Session"",
                    ""status"": ""running"",
                    ""agent"": ""assistant"",
                    ""model"": ""claude-3.5-sonnet"",
                    ""directory"": ""C:\\projects\\test"",
                    ""time"": {
                        ""created"": 1234567890000,
                        ""updated"": 1234567890000
                    }
                }
            }";

            // Act
            var evt = JsonConvert.DeserializeObject<EventSessionCreated>(json, _settings);

            // Assert
            evt.Should().NotBeNull();
            evt.Type.Should().Be(EventSessionCreated.TypeValue.SessionCreated);
            evt.Session.Should().NotBeNull();
            evt.Session.Id.Should().Be("sess-abc123");
            evt.Session.Title.Should().Be("My Session");
            evt.Session.Directory.Should().Be("C:\\projects\\test");
        }

        [Fact]
        public void EventMessageUpdated_Deserialization_PreservesMessageData()
        {
            // Arrange - Realistic message updated event
            var json = @"{
                ""type"": ""message.updated"",
                ""sessionID"": ""sess-123"",
                ""message"": {
                    ""role"": ""assistant"",
                    ""parts"": [
                        { ""type"": ""text"", ""text"": ""Updated content"" }
                    ]
                }
            }";

            // Act
            var evt = JsonConvert.DeserializeObject<EventMessageUpdated>(json, _settings);

            // Assert
            evt.Should().NotBeNull();
            evt.Type.Should().Be(EventMessageUpdated.TypeValue.MessageUpdated);
            evt.SessionID.Should().Be("sess-123");
            evt.Message.Should().NotBeNull();
        }

        #endregion

        #region SessionMessage Models

        [Fact]
        public void SessionMessageAgentSwitched_Deserialization_PreservesAgentInfo()
        {
            // Arrange - Realistic agent switch event
            var json = @"{
                ""type"": ""session.next.agent.switched"",
                ""sessionID"": ""sess-123"",
                ""fromAgent"": ""assistant"",
                ""toAgent"": ""planner""
            }";

            // Act
            var msg = JsonConvert.DeserializeObject<SessionMessageAgentSwitched>(json, _settings);

            // Assert
            msg.Should().NotBeNull();
            msg.Type.Should().Be(SessionMessageAgentSwitched.TypeValue.SessionNextAgentSwitched);
            msg.SessionID.Should().Be("sess-123");
            msg.FromAgent.Should().Be("assistant");
            msg.ToAgent.Should().Be("planner");
        }

        #endregion

        #region Question Models

        [Fact]
        public void QuestionReplied_Deserialization_PreservesAnswers()
        {
            // Arrange - Realistic question reply
            var json = @"{
                ""sessionID"": ""sess-123"",
                ""requestID"": ""req-456"",
                ""answers"": [""answer1"", ""answer2""]
            }";

            // Act
            var question = JsonConvert.DeserializeObject<QuestionReplied>(json, _settings);

            // Assert
            question.Should().NotBeNull();
            question.SessionID.Should().Be("sess-123");
            question.RequestID.Should().Be("req-456");
            question.Answers.Should().HaveCount(2);
            question.Answers[0].Should().Be("answer1");
        }

        [Fact]
        public void QuestionRejected_Deserialization_PreservesIds()
        {
            // Arrange
            var json = @"{
                ""sessionID"": ""sess-123"",
                ""requestID"": ""req-456""
            }";

            // Act
            var question = JsonConvert.DeserializeObject<QuestionRejected>(json, _settings);

            // Assert
            question.Should().NotBeNull();
            question.SessionID.Should().Be("sess-123");
            question.RequestID.Should().Be("req-456");
        }

        #endregion

        #region ToolState Models

        [Fact]
        public void ToolStateCompleted_Deserialization_PreservesToolData()
        {
            // Arrange - Realistic completed tool state
            var json = @"{
                ""status"": ""completed"",
                ""name"": ""shell"",
                ""input"": { ""command"": ""ls -la"" },
                ""output"": ""file1.txt\\nfile2.txt"",
                ""duration"": 150
            }";

            // Act
            var state = JsonConvert.DeserializeObject<ToolStateCompleted>(json, _settings);

            // Assert
            state.Should().NotBeNull();
            state.Status.Should().Be("completed");
            state.Name.Should().Be("shell");
            state.Input.Should().NotBeNull();
            state.Output.Should().Be("file1.txt\\nfile2.txt");
            state.Duration.Should().Be(150);
        }

        [Fact]
        public void ToolStateRunning_Deserialization_PreservesProgress()
        {
            // Arrange
            var json = @"{
                ""status"": ""running"",
                ""name"": ""task"",
                ""input"": { ""prompt"": ""Do something"" },
                ""progress"": ""50% complete""
            }";

            // Act
            var state = JsonConvert.DeserializeObject<ToolStateRunning>(json, _settings);

            // Assert
            state.Should().NotBeNull();
            state.Status.Should().Be("running");
            state.Progress.Should().Be("50% complete");
        }

        #endregion

        #region Permission Models

        [Fact]
        public void PermissionObjectConfig_Deserialization_PreservesActions()
        {
            // Arrange - Realistic permission config
            var json = @"{
                ""write"": { ""allow"": true, ""directories"": [""C:\\proj""] },
                ""execute"": { ""allow"": false },
                ""read"": { ""allow"": true }
            }";

            // Act
            var config = JsonConvert.DeserializeObject<PermissionObjectConfig>(json, _settings);

            // Assert
            config.Should().NotBeNull();
            config.Should().ContainKey("write");
            config.Should().ContainKey("execute");
            config.Should().ContainKey("read");
        }

        [Fact]
        public void PermissionRule_Deserialization_PreservesRuleData()
        {
            // Arrange
            var json = @"{
                ""action"": ""write"",
                ""pattern"": ""*.cs"",
                ""allow"": true,
                ""reason"": ""Allow C# file edits""
            }";

            // Act
            var rule = JsonConvert.DeserializeObject<PermissionRule>(json, _settings);

            // Assert
            rule.Should().NotBeNull();
            rule.Action.Should().Be("write");
            rule.Pattern.Should().Be("*.cs");
            rule.Allow.Should().BeTrue();
            rule.Reason.Should().Be("Allow C# file edits");
        }

        #endregion

        #region Round-trip Serialization

        [Fact]
        public void TextPart_RoundTrip_SerializationPreservesData()
        {
            // Arrange
            var original = new TextPart
            {
                Type = "text",
                Text = "Test content",
                Delta = "Test content"
            };

            // Act - Serialize and deserialize
            var json = JsonConvert.SerializeObject(original, _settings);
            var deserialized = JsonConvert.DeserializeObject<TextPart>(json, _settings);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Type.Should().Be(original.Type);
            deserialized.Text.Should().Be(original.Text);
            deserialized.Delta.Should().Be(original.Delta);
        }

        [Fact]
        public void EventTuiToastShow_RoundTrip_SerializationPreservesData()
        {
            // Arrange
            var original = new EventTuiToastShow
            {
                Type = EventTuiToastShow.TypeValue.TuiToastShow,
                Title = "Info",
                Message = "Test message",
                Variant = "info",
                Duration = 3000
            };

            // Act
            var json = JsonConvert.SerializeObject(original, _settings);
            var deserialized = JsonConvert.DeserializeObject<EventTuiToastShow>(json, _settings);

            // Assert
            deserialized.Should().NotBeNull();
            deserialized.Type.Should().Be(original.Type);
            deserialized.Title.Should().Be(original.Title);
            deserialized.Message.Should().Be(original.Message);
            deserialized.Variant.Should().Be(original.Variant);
            deserialized.Duration.Should().Be(original.Duration);
        }

        #endregion
    }
}
