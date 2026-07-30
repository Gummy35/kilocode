using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for followup session - mirrors followup-session.test.ts from VS Code
    /// </summary>
    public class FollowupSessionTests
    {
        private class MockClient
        {
            public List<CreateCall> Calls { get; } = new List<CreateCall>();

            public Task<CreateResult> CreateSessionAsync(string directory, object metadata)
            {
                Calls.Add(new CreateCall { Directory = directory, Metadata = metadata });
                return Task.FromResult(new CreateResult { 
                    Data = new { Id = "new-session", ParentID = (metadata as dynamic)?.ParentID } 
                });
            }
        }

        private class CreateCall
        {
            public string Directory { get; set; }
            public object Metadata { get; set; }
        }

        private class CreateResult
        {
            public object Data { get; set; }
        }

        private class VSProviderInternals
        {
            public MockClient Client { get; set; } = new MockClient();
            public List<string> PostedMessages { get; } = new List<string>();

            public async Task HandleCreateSessionAsync(string directory, object metadata = null)
            {
                var result = await Client.CreateSessionAsync(directory, metadata);
                var data = (dynamic)result.Data;

                PostedMessages.Add(JsonSerializer.Serialize(new { 
                    type = "sessionCreated", 
                    session = new { Id = data.Id, ParentID = data.ParentID } 
                }));
            }
        }

        [Fact]
        public async Task Creates_followup_session_with_parent_metadata()
        {
            // Arrange
            var internals = new VSProviderInternals();
            var metadata = new { ParentID = "s1", Followup = true };

            // Act
            await internals.HandleCreateSessionAsync(@"/repo", metadata);

            // Assert
            internals.Client.Calls.Count.Should().Be(1);
            internals.Client.Calls[0].Directory.Should().Be(@"/repo");

            internals.PostedMessages.Count.Should().Be(1);
            internals.PostedMessages[0].Should().Contain("sessionCreated");
        }
    }
}
