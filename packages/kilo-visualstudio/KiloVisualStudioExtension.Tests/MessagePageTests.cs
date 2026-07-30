using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for message page fetching - mirrors message-page.test.ts from VS Code
    /// These tests verify cursor handling and pagination logic
    /// </summary>
    public class MessagePageFetchTests
    {
        private class MPMsg
        {
            public MPMsgInfo Info { get; set; }
            public List<object> Parts { get; set; }
        }

        private class MPMsgInfo
        {
            public string Id { get; set; }
            public string Role { get; set; }
            public MPTimeInfo Time { get; set; }
        }

        private class MPTimeInfo
        {
            public long Created { get; set; }
        }

        private class MPClient
        {
            public List<MPPageData> Pages { get; set; }
            public List<MPCall> Calls { get; } = new List<MPCall>();
            private int _index = 0;

            public Task<MPPageResult> MessagesAsync(string sessionID, string directory, int limit, string before = null)
            {
                Calls.Add(new MPCall { Before = before, Limit = limit });
                
                if (_index >= Pages.Count)
                    return Task.FromException<MPPageResult>(new Exception("no more mock pages"));

                var page = Pages[_index++];
                return Task.FromResult(new MPPageResult 
                { 
                    Items = page.Items,
                    Cursor = page.Cursor
                });
            }

            public void Reset() => _index = 0;
        }

        private class MPCall
        {
            public string Before { get; set; }
            public int Limit { get; set; }
        }

        private class MPPageData
        {
            public List<MPMsg> Items { get; set; }
            public string Cursor { get; set; }
        }

        private class MPPageResult
        {
            public List<MPMsg> Items { get; set; }
            public string Cursor { get; set; }
        }

        private static async Task<MPPageResult> FetchMPMessagePage(MPClient client, string sessionID, string workspaceDir, int limit)
        {
            var result = await client.MessagesAsync(sessionID, workspaceDir, limit);

            if (!string.IsNullOrEmpty(result.Cursor))
                return result;

            if (result.Items.Count == limit)
            {
                var oldest = result.Items.Last();
                var cursorData = new { id = oldest.Info.Id, time = oldest.Info.Time.Created };
                var json = JsonSerializer.Serialize(cursorData);
                result.Cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            }

            return result;
        }

        private static MPMsg CreateMPMsg(string id, string role, long time)
        {
            return new MPMsg
            {
                Info = new MPMsgInfo { Id = id, Role = role, Time = new MPTimeInfo { Created = time } },
                Parts = new List<object>()
            };
        }

        [Fact]
        public async Task Returns_server_cursor_when_header_is_present()
        {
            // Arrange
            var client = new MPClient
            {
                Pages = new List<MPPageData>
                {
                    new MPPageData
                    {
                        Items = new List<MPMsg>
                        {
                            CreateMPMsg("m1", "user", 1),
                            CreateMPMsg("m2", "assistant", 2),
                            CreateMPMsg("m3", "user", 3)
                        },
                        Cursor = "server-cursor-abc"
                    }
                }
            };

            // Act
            var page = await FetchMPMessagePage(client, "s1", @"/repo", 3);

            // Assert
            page.Cursor.Should().Be("server-cursor-abc");
        }

        [Fact]
        public async Task Synthesizes_cursor_when_header_omitted_but_page_is_full()
        {
            // Arrange
            var client = new MPClient
            {
                Pages = new List<MPPageData>
                {
                    new MPPageData
                    {
                        Items = new List<MPMsg>
                        {
                            CreateMPMsg("m1", "user", 10),
                            CreateMPMsg("m2", "assistant", 20),
                            CreateMPMsg("m3", "user", 30),
                            CreateMPMsg("m4", "assistant", 40)
                        }
                    }
                }
            };

            // Act
            var page = await FetchMPMessagePage(client, "s1", @"/repo", 4);

            // Assert
            page.Cursor.Should().NotBeNull();
            
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(page.Cursor));
            var cursorData = JsonSerializer.Deserialize<JsonElement>(json);
            cursorData.GetProperty("id").GetString().Should().Be("m1");
            cursorData.GetProperty("time").GetInt64().Should().Be(10);
        }

        [Fact]
        public async Task Leaves_cursor_null_when_header_omitted_AND_page_not_full()
        {
            // Arrange
            var client = new MPClient
            {
                Pages = new List<MPPageData>
                {
                    new MPPageData
                    {
                        Items = new List<MPMsg>
                        {
                            CreateMPMsg("m1", "user", 1),
                            CreateMPMsg("m2", "assistant", 2)
                        }
                    }
                }
            };

            // Act
            var page = await FetchMPMessagePage(client, "s1", @"/repo", 4);

            // Assert
            page.Cursor.Should().BeNull();
        }
    }
}

