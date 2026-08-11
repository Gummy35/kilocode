using System;
using System.Collections.Generic;
using FluentAssertions;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.ApiClient.Json;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for NSwag-generated polymorphic model round-trip serialization.
    /// Validates that concrete types can be serialized and deserialized using
    /// the PolymorphicDeserializer discriminator-based methods.
    /// 
    /// Each test follows the pattern:
    /// 1. Create a concrete type instance
    /// 2. Serialize to JSON
    /// 3. Parse to JToken
    /// 4. Deserialize using PolymorphicDeserializer.DeserializePart/ToolState/Message
    /// 5. Verify the result is the correct type with matching properties
    /// </summary>
    public class NswagPolymorphicModelTests
    {
        private readonly JsonSerializer _polymorphicSerializer;

        public NswagPolymorphicModelTests()
        {
            _polymorphicSerializer = KiloJsonSerializer.Create();
        }

        #region Part Round-Trip Tests

        [Fact]
        public void TextPart_RoundTrip_DeserializePart_PreservesData()
        {
            // Arrange
            var original = new TextPart
            {
                Id = "part-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = TextPartType.Text,
                Text = "Hello world",
                Synthetic = false,
                Ignored = false
            };

            // Act - Serialize and deserialize through polymorphic deserializer
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<TextPart>();
            var deserialized = (TextPart)result;
            deserialized.Type.Should().Be(original.Type);
            deserialized.Text.Should().Be(original.Text);
            deserialized.Id.Should().Be(original.Id);
            deserialized.SessionID.Should().Be(original.SessionID);
            deserialized.MessageID.Should().Be(original.MessageID);
        }

        [Fact]
        public void ToolPart_RoundTrip_DeserializePart_PreservesData()
        {
            // Arrange
            var toolState = new ToolStateCompleted
            {
                Status = ToolStateCompletedStatus.Completed,
                Input = new { path = "test.txt" },
                Output = "file contents here",
                Title = "File read",
                Metadata = new { },
                Time = new Time10 { Start = 100, End = 200 }
            };

            var original = new ToolPart
            {
                Id = "part-tool-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = ToolPartType.Tool,
                CallID = "call-abc",
                Tool = "read",
                State = toolState  // Now works due to inheritance
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<ToolPart>();
            var deserialized = (ToolPart)result;
            deserialized.Type.Should().Be(ToolPartType.Tool);
            deserialized.Tool.Should().Be("read");
            deserialized.CallID.Should().Be("call-abc");
            deserialized.Id.Should().Be("part-tool-123");
            // Nested State should now be deserialized as ToolStateCompleted
            deserialized.State.Should().BeOfType<ToolStateCompleted>();
            var completedState = (ToolStateCompleted)deserialized.State;
            completedState.Output.Should().Be("file contents here");
            completedState.Title.Should().Be("File read");
        }

        [Fact]
        public void ReasoningPart_RoundTrip_DeserializePart_PreservesData()
        {
            // Arrange
            var original = new ReasoningPart
            {
                Id = "part-reason-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = ReasoningPartType.Reasoning,
                Text = "Let me think about this..."
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<ReasoningPart>();
            var deserialized = (ReasoningPart)result;
            deserialized.Type.Should().Be(original.Type);
            deserialized.Text.Should().Be(original.Text);
            deserialized.Id.Should().Be(original.Id);
        }

        [Fact]
        public void FilePart_RoundTrip_DeserializePart_PreservesData()
        {
            // Arrange
            var original = new FilePart
            {
                Id = "part-file-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = FilePartType.File,
                Mime = "text/plain",
                Url = "file:///C:/proj/src/test.cs"
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<FilePart>();
            var deserialized = (FilePart)result;
            deserialized.Type.Should().Be(FilePartType.File);
            deserialized.Mime.Should().Be(original.Mime);
            deserialized.Url.Should().Be(original.Url);
            deserialized.Id.Should().Be(original.Id);
        }

        [Fact]
        public void FilePart_WithSource_RoundTrip_DeserializePart_PreservesSource()
        {
            // Arrange - FilePart with nested FileSource
            var original = new FilePart
            {
                Id = "part-file-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = FilePartType.File,
                Mime = "text/plain",
                Url = "file:///C:/proj/src/test.cs",
                Source = new FileSource
                {
                    Type = FileSourceType.File,
                    Path = "C:\\proj\\src\\test.cs",
                    Text = new FilePartSourceText
                    {
                        Value = "public class Test { }",
                        Start = 0,
                        End = 20
                    }
                }
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<FilePart>();
            var deserialized = (FilePart)result;
            deserialized.Type.Should().Be(FilePartType.File);
            deserialized.Mime.Should().Be("text/plain");
            deserialized.Source.Should().NotBeNull();
            deserialized.Source.Should().BeOfType<FileSource>();
            var fileSource = (FileSource)deserialized.Source;
            fileSource.Path.Should().Be("C:\\proj\\src\\test.cs");
            fileSource.Text.Should().NotBeNull();
            fileSource.Text.Value.Should().Be("public class Test { }");
        }

        [Fact]
        public void AgentPart_RoundTrip_DeserializePart_PreservesData()
        {
            // Arrange
            var original = new AgentPart
            {
                Id = "part-agent-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = AgentPartType.Agent,
                Name = "assistant"
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<AgentPart>();
            var deserialized = (AgentPart)result;
            deserialized.Type.Should().Be(AgentPartType.Agent);
            deserialized.Id.Should().Be(original.Id);
            deserialized.Name.Should().Be(original.Name);
        }

        [Fact]
        public void CompactionPart_RoundTrip_DeserializePart_PreservesData()
        {
            // Arrange
            var original = new CompactionPart
            {
                Id = "part-compaction-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = CompactionPartType.Compaction,
                Auto = true
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<CompactionPart>();
            var deserialized = (CompactionPart)result;
            deserialized.Type.Should().Be(CompactionPartType.Compaction);
            deserialized.Id.Should().Be(original.Id);
            deserialized.Auto.Should().Be(original.Auto);
        }

        [Fact]
        public void PatchPart_RoundTrip_DeserializePart_PreservesData()
        {
            // Arrange
            var original = new PatchPart
            {
                Id = "part-patch-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = PatchPartType.Patch,
                Hash = "abc123",
                Files = new System.Collections.Generic.List<string> { "file1.cs", "file2.cs" }
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<PatchPart>();
            var deserialized = (PatchPart)result;
            deserialized.Type.Should().Be(PatchPartType.Patch);
            deserialized.Id.Should().Be(original.Id);
            deserialized.Hash.Should().Be(original.Hash);
            deserialized.Files.Count.Should().Be(2);
        }

        [Fact]
        public void SnapshotPart_RoundTrip_DeserializePart_PreservesData()
        {
            // Arrange
            var original = new SnapshotPart
            {
                Id = "part-snapshot-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = SnapshotPartType.Snapshot,
                Snapshot = "snapshot-data-here"
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<SnapshotPart>();
            var deserialized = (SnapshotPart)result;
            deserialized.Type.Should().Be(SnapshotPartType.Snapshot);
            deserialized.Id.Should().Be(original.Id);
            deserialized.Snapshot.Should().Be(original.Snapshot);
        }

        [Fact]
        public void StepFinishPart_RoundTrip_DeserializePart_PreservesData()
        {
            // Arrange
            var original = new StepFinishPart
            {
                Id = "part-stepfinish-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = StepFinishPartType.StepFinish,
                Reason = "step completed"
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<StepFinishPart>();
            var deserialized = (StepFinishPart)result;
            deserialized.Type.Should().Be(StepFinishPartType.StepFinish);
            deserialized.Id.Should().Be(original.Id);
            deserialized.Reason.Should().Be(original.Reason);
        }

        [Fact]
        public void StepStartPart_RoundTrip_DeserializePart_PreservesData()
        {
            // Arrange
            var original = new StepStartPart
            {
                Id = "part-stepstart-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = StepStartPartType.StepStart
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<StepStartPart>();
            var deserialized = (StepStartPart)result;
            deserialized.Type.Should().Be(StepStartPartType.StepStart);
            deserialized.Id.Should().Be(original.Id);
        }

        [Fact]
        public void SubtaskPart_RoundTrip_DeserializePart_PreservesData()
        {
            // Arrange
            var original = new SubtaskPart
            {
                Id = "part-subtask-123",
                SessionID = "sess-456",
                MessageID = "msg-789",
                Type = SubtaskPartType.Subtask,
                Prompt = "do something",
                Description = "test subtask",
                Agent = "assistant"
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializePart(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<SubtaskPart>();
            var deserialized = (SubtaskPart)result;
            deserialized.Type.Should().Be(SubtaskPartType.Subtask);
            deserialized.Id.Should().Be(original.Id);
            deserialized.Prompt.Should().Be(original.Prompt);
            deserialized.Description.Should().Be(original.Description);
        }

        #endregion

        #region ToolState Round-Trip Tests

        [Fact]
        public void ToolStateCompleted_RoundTrip_DeserializeToolState_PreservesData()
        {
            // Arrange
            var original = new ToolStateCompleted
            {
                Status = ToolStateCompletedStatus.Completed,
                Input = new { command = "ls -la" },
                Output = "file1.txt\nfile2.txt",
                Title = "Shell command",
                Metadata = new { },
                Time = new Time10 { Start = 100, End = 200 },
                Attachments = new List<FilePart>()
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializeToolState(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<ToolStateCompleted>();
            var deserialized = (ToolStateCompleted)result;
            deserialized.Status.Should().Be(original.Status);
            deserialized.Output.Should().Be(original.Output);
            deserialized.Title.Should().Be(original.Title);
        }

        [Fact]
        public void ToolStateRunning_RoundTrip_DeserializeToolState_PreservesData()
        {
            // Arrange
            var original = new ToolStateRunning
            {
                Status = ToolStateRunningStatus.Running,
                Input = new { prompt = "Do something" },
                Title = "Running task",
                Metadata = new { },
                Time = new Time9 { Start = 100 }
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializeToolState(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<ToolStateRunning>();
            var deserialized = (ToolStateRunning)result;
            deserialized.Status.Should().Be(original.Status);
            deserialized.Title.Should().Be(original.Title);
        }

        [Fact]
        public void ToolStatePending_RoundTrip_DeserializeToolState_PreservesData()
        {
            // Arrange
            var original = new ToolStatePending
            {
                Status = ToolStatePendingStatus.Pending,
                Input = new { prompt = "Waiting" },
                Raw = "raw content"
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializeToolState(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<ToolStatePending>();
            var deserialized = (ToolStatePending)result;
            deserialized.Status.Should().Be(original.Status);
            deserialized.Raw.Should().Be(original.Raw);
        }

        [Fact]
        public void ToolStateError_RoundTrip_DeserializeToolState_PreservesData()
        {
            // Arrange
            var original = new ToolStateError
            {
                Status = ToolStateErrorStatus.Error,
                Input = new { command = "fail" },
                Error = "Something went wrong",
                Metadata = new { },
                Time = new Time11 { Start = 100 }
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializeToolState(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<ToolStateError>();
            var deserialized = (ToolStateError)result;
            deserialized.Status.Should().Be(original.Status);
            deserialized.Error.Should().Be(original.Error);
        }

        #endregion

        #region Message Round-Trip Tests

        [Fact]
        public void UserMessage_RoundTrip_DeserializeMessage_PreservesData()
        {
            // Arrange
            var original = new UserMessage
            {
                Id = "msg-123",
                SessionID = "sess-456",
                Role = UserMessageRole.User,
                Time = new Time5 { Created = 100 },
                Agent = "assistant",
                Model = new Model3 { ProviderID = "console", ModelID = "claude-3.5-sonnet" },
                System = "system prompt"
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializeMessage(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<UserMessage>();
            var deserialized = (UserMessage)result;
            deserialized.Role.Should().Be(original.Role);
            deserialized.Id.Should().Be(original.Id);
            deserialized.SessionID.Should().Be(original.SessionID);
        }

        [Fact]
        public void AssistantMessage_RoundTrip_DeserializeMessage_PreservesData()
        {
            // Arrange
            var original = new AssistantMessage
            {
                Id = "msg-456",
                SessionID = "sess-789",
                Role = AssistantMessageRole.Assistant,
                Time = new Time6 { Created = 100 },
                ParentID = "parent-123",
                ModelID = "claude-3.5-sonnet",
                ProviderID = "console",
                Mode = "chat",
                Agent = "assistant",
                Path = new Path2 { Cwd = "C:\\proj", Root = "C:\\proj" },
                Cost = 0.002
            };

            // Act
            var json = JsonConvert.SerializeObject(original);
            var token = JToken.Parse(json);
            var result = PolymorphicDeserializer.DeserializeMessage(token, _polymorphicSerializer);

            // Assert
            result.Should().BeOfType<AssistantMessage>();
            var deserialized = (AssistantMessage)result;
            deserialized.Role.Should().Be(original.Role);
            deserialized.Id.Should().Be(original.Id);
            deserialized.SessionID.Should().Be(original.SessionID);
            deserialized.ModelID.Should().Be(original.ModelID);
        }

        #endregion
    }
}
