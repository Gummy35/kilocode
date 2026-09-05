using KiloExtensionDTOs;
using KiloVisualStudioExtension.ApiClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace KiloVisualStudioExtension.Services
{
    public class PermissionService : ServiceProviderServiceBase
    {
        public class PermissionContext
        {
            public KiloApiClient? Client { get; set; }
            public string? CurrentSessionId { get; set; }
            public HashSet<string> TrackedSessionIds { get; set; } = new();
            public Dictionary<string, string> SessionDirectories { get; set; } = new();
            public Func<string[]>? ExtraDirectories { get; set; }
            public Action<IWebviewMessage?> PostMessage { get; set; } = _ => { };
            public Func<string?, string> GetWorkspaceDirectory { get; set; } = _ => "";
            public Action<string, string> RecordPermissionDirectory { get; set; } = (_, _) => { };
            public Func<string, string?> GetPermissionDirectory { get; set; } = _ => null;
            public Action<string> ClearPermissionDirectory { get; set; } = _ => { };
            public Action<HashSet<string>, HashSet<string>?> PrunePermissionDirectories { get; set; } = (_, _) => { };
        }

        public PermissionService(ServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>()
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        public static string[] RecoveryDirs(string workspace, Dictionary<string, string> dirs, string[]? extra = null)
        {
            var set = new HashSet<string> { workspace };
            foreach (var dir in dirs.Values) set.Add(dir);
            if (extra != null)
            {
                foreach (var e in extra) set.Add(e);
            }
            return set.ToArray();
        }

        public static IEnumerable<PermissionRequest> RecoverablePermissions(
            List<PermissionRequest> perms,
            HashSet<string> tracked,
            HashSet<string> seen)
        {
            foreach (var perm in perms)
            {
                if (seen.Contains(perm.Id)) continue;
                seen.Add(perm.Id);
                if (tracked.Contains(perm.SessionID))
                    yield return perm;
            }
        }

        private static bool IsNotFoundError(object? error)
        {
            if (error == null) return false;

            dynamic? record = value =>
                value != null && value is System.Collections.IDictionary dict
                    ? dict
                    : value != null && value.GetType().IsClass
                        ? value
                        : null;

            var obj = record(error);
            if (obj == null) return false;

            var GetProp = (dynamic? o, string name) =>
                o != null && o is System.Collections.IDictionary d && d.Contains(name)
                    ? d[name]
                    : o != null && o.GetType().GetProperty(name) != null
                        ? o.GetType().GetProperty(name)!.GetValue(o)
                        : null;

            var cause = GetProp(obj, "cause");
            var body = GetProp(cause, "body");

            var check = new[] { obj, GetProp(obj, "data"), cause, body, GetProp(body, "data") };
            return check.Any(v =>
            {
                var name = GetProp(v, "name")?.ToString();
                var status = GetProp(v, "status");
                return name == "NotFoundError" || (status != null && Convert.ToInt32(status) == 404);
            });
        }

        public async Task HandlePermissionResponse(
            PermissionContext ctx,
            string permissionId,
            string sessionID,
            string response,
            string[] approvedAlways,
            string[] deniedAlways)
        {
            if (ctx.Client == null)
            {
                ctx.PostMessage(new { type = "permissionError", permissionID = permissionId });
                return;
            }

            var target = string.IsNullOrEmpty(sessionID) ? ctx.CurrentSessionId : sessionID;
            if (string.IsNullOrEmpty(target))
            {
                Console.Error.WriteLine("[Kilo New] KiloProvider: No sessionID for permission response");
                ctx.PostMessage(new { type = "permissionError", permissionID = permissionId });
                return;
            }

            var dir = ctx.GetPermissionDirectory(permissionId) ?? ctx.GetWorkspaceDirectory(target);

            void StaleCleanup()
            {
                ctx.ClearPermissionDirectory(permissionId);
                ctx.PostMessage(new { type = "permissionError", permissionID = permissionId, stale = true });
                _ = FetchAndSendPendingPermissions(ctx);
            }

            if (approvedAlways.Length > 0 || deniedAlways.Length > 0)
            {
                var saveResult = await ctx.Client.Permission_SaveAlwaysRulesAsync(
                    new
                    {
                        RequestId = permissionId,
                        Directory = dir,
                        ApprovedAlways = approvedAlways,
                        DeniedAlways = deniedAlways
                    },
                    new { ThrowOnError = true })
                    .ContinueWith(_ => "ok" as string)
                    .CatchAsync<PermissionRequest>(error =>
                    {
                        if (IsNotFoundError(error)) return Task.FromResult("stale" as string);
                        Console.Error.WriteLine($"[Kilo New] KiloProvider: Failed to save always-rules: {error}");
                        ctx.PostMessage(new { type = "permissionError", permissionID = permissionId });
                        return Task.FromResult("error" as string);
                    });

                if (saveResult == "stale")
                {
                    StaleCleanup();
                    return;
                }
                if (saveResult == "error") return;
            }

            var replyResult = await ctx.Client.Permission_replyAsync(
                new
                {
                    RequestId = permissionId,
                    Reply = response,
                    Directory = dir
                },
                new { ThrowOnError = true })
                .ContinueWith(_ => "ok" as string)
                .CatchAsync<PermissionRequest>(error =>
                {
                    if (IsNotFoundError(error)) return Task.FromResult("stale" as string);
                    Console.Error.WriteLine($"[Kilo New] KiloProvider: Failed to respond to permission: {error}");
                    ctx.PostMessage(new { type = "permissionError", permissionID = permissionId });
                    return Task.FromResult("error" as string);
                });

            if (replyResult == "stale")
            {
                StaleCleanup();
            }
        }

        public async Task FetchAndSendPendingPermissions(PermissionContext ctx)
        {
            if (ctx.Client == null) return;

            try
            {
                var extra = ctx.ExtraDirectories?.Invoke() ?? Array.Empty<string>();
                var dirs = RecoveryDirs(ctx.GetWorkspaceDirectory(), ctx.SessionDirectories, extra);

                var seen = new HashSet<string>();
                var valid = new HashSet<string>();

                foreach (var dir in dirs)
                {
                    var result = await ctx.Client.Permission_ListAsync(new { Directory = dir });
                    if (result.Error != null)
                    {
                        Console.Error.WriteLine($"[Kilo New] KiloProvider: Failed to fetch pending permissions for {dir}: {result.Error}");
                        continue;
                    }

                    valid.Add(dir);
                    if (result.Data == null) continue;

                    foreach (var perm in RecoverablePermissions(result.Data, ctx.TrackedSessionIds, seen))
                    {
                        ctx.RecordPermissionDirectory(perm.Id, dir);
                        ctx.PostMessage(new
                        {
                            type = "permissionRequest",
                            permission = new
                            {
                                id = perm.Id,
                                sessionID = perm.SessionId,
                                toolName = perm.Permission,
                                patterns = perm.Patterns,
                                always = perm.Always,
                                args = perm.Metadata,
                                message = $"Permission required: {perm.Permission}",
                                tool = perm.Tool
                            }
                        });
                    }
                }

                ctx.PrunePermissionDirectories(seen, valid);
            }
            catch (Exception error)
            {
                Console.Error.WriteLine($"[Kilo New] KiloProvider: Failed to fetch pending permissions: {error.Message}");
            }
        }
    }

    public static class TaskExtensions
    {
        public static async Task<TOut> CatchAsync<TIn>(this Task<TIn> task, Func<object, Task<TOut>> handler)
        {
            try
            {
                return await task;
            }
            catch (Exception ex)
            {
                return await handler(ex);
            }
        }
    }
}
