using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using KiloVisualStudioExtension.Services;

namespace KiloVisualStudioExtension.Tests
{
    public class MessagePageFetchTests
    {
        private class TestClient : MessagePageFetcher.IClient
        {
            public List<PageData> Pages { get; set; }
            public List<Call> Calls { get; } = new List<Call>();
            private int _index = 0;

            public Task<MessagePageFetcher.PageResult> MessagesAsync(string sessionID, string directory, int limit, string before = null)
            {
                Calls.Add(new Call { Before = before, Limit = limit });
                
                if (_index >= Pages.Count)
                    return Task.FromException<MessagePageFetcher.PageResult>(new Exception("no more mock pages"));

                var page = Pages[_index++];
                return Task.FromResult(new MessagePageFetcher.PageResult 
                { 
                    Items = page.Items,
                    Cursor = page.Cursor
                });
            }

            public void Reset() => _index = 0;
        }

        private class Call
        {
            public string Before { get; set; }
            public int Limit { get; set; }
        }

        private class PageData
        {
            public List<MessagePageFetcher.Message> Items { get; set; }
            public string Cursor { get; set; }
        }

        private static MessagePageFetcher.Message CreateMsg(string id, string role, long time)
        {
            return new MessagePageFetcher.Message
            {
                Info = new MessagePageFetcher.MessageInfo 
                { 
                    Id = id, 
                    Role = role, 
                    Time = new MessagePageFetcher.TimeInfo { Created = time } 
                },
                Parts = new List<object>()
            };
        }

        [Fact]
        public async Task Returns_server_cursor_when_header_is_present()
        {
            var client = new TestClient
            {
                Pages = new List<PageData>
                {
                    new PageData
                    {
                        Items = new List<MessagePageFetcher.Message>
                        {
                            CreateMsg("m1", "user", 1),
                            CreateMsg("m2", "assistant", 2),
                            CreateMsg("m3", "user", 3)
                        },
                        Cursor = "server-cursor-abc"
                    }
                }
            };

            var fetcher = new MessagePageFetcher();
            var page = await fetcher.FetchMessagePageAsync(client, "s1", @"/repo", 3);

            page.Cursor.Should().Be("server-cursor-abc");
        }

        [Fact]
        public async Task Synthesizes_cursor_when_header_omitted_but_page_is_full()
        {
            var client = new TestClient
            {
                Pages = new List<PageData>
                {
                    new PageData
                    {
                        Items = new List<MessagePageFetcher.Message>
                        {
                            CreateMsg("m1", "user", 10),
                            CreateMsg("m2", "assistant", 20),
                            CreateMsg("m3", "user", 30),
                            CreateMsg("m4", "assistant", 40)
                        }
                    }
                }
            };

            var fetcher = new MessagePageFetcher();
            var page = await fetcher.FetchMessagePageAsync(client, "s1", @"/repo", 4);

            page.Cursor.Should().NotBeNull();
            
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(page.Cursor));
            var cursorData = JsonSerializer.Deserialize<JsonElement>(json);
            cursorData.GetProperty("id").GetString().Should().Be("m1");
            cursorData.GetProperty("time").GetInt64().Should().Be(10);
        }

        [Fact]
        public async Task Leaves_cursor_null_when_header_omitted_AND_page_not_full()
        {
            var client = new TestClient
            {
                Pages = new List<PageData>
                {
                    new PageData
                    {
                        Items = new List<MessagePageFetcher.Message>
                        {
                            CreateMsg("m1", "user", 1),
                            CreateMsg("m2", "assistant", 2)
                        }
                    }
                }
            };

            var fetcher = new MessagePageFetcher();
            var page = await fetcher.FetchMessagePageAsync(client, "s1", @"/repo", 4);

            page.Cursor.Should().BeNull();
        }
    }
}

