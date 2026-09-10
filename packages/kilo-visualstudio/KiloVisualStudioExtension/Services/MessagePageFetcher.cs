using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    public class MessagePageFetcher
    {
        public interface IClient
        {
            Task<PageResult> MessagesAsync(string sessionID, string directory, int limit, string before = null);
        }

        public class PageResult
        {
            public List<Message> Items { get; set; }
            public string Cursor { get; set; }
        }

        public class Message
        {
            public MessageInfo Info { get; set; }
            public List<object> Parts { get; set; }
        }

        public class MessageInfo
        {
            public string Id { get; set; }
            public string Role { get; set; }
            public TimeInfo Time { get; set; }
        }

        public class TimeInfo
        {
            public long Created { get; set; }
        }

        public async Task<PageResult> FetchMessagePageAsync(IClient client, string sessionID, string workspaceDir, int limit)
        {
            var result = await client.MessagesAsync(sessionID, workspaceDir, limit);

            if (!string.IsNullOrEmpty(result.Cursor))
                return result;

            if (result.Items.Count == limit)
            {
                var oldest = result.Items.First();
                var cursorData = new { id = oldest.Info.Id, time = oldest.Info.Time.Created };
                var json = JsonSerializer.Serialize(cursorData);
                result.Cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            }

            return result;
        }
    }
}
