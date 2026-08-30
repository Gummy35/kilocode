using EnvDTE;
using KiloVisualStudioExtension.ApiClient;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using WebviewMessage = KiloExtensionDTOs.Sessions.Message;

namespace KiloVisualStudioExtension.Utils
{
  internal class SlimUtils
  {

    /// <summary>
    /// Strips patch data from summary diffs in user message info.
    /// Matches VS Code's slimInfo pattern - removes large patch text that the webview doesn't need.
    /// Returns the same object if no summary or no patches found.
    /// </summary>
    public static UserMessage SlimInfo(UserMessage message)
    {
      if (message == null)
        return null;

      // Only UserMessage has Summary, and only if it has diffs with patches do we transform
      if (message.Summary == null || message.Summary.Diffs == null || message.Summary.Diffs.Count == 0)
        return message;

      // Check if any diff has a patch
      if (!message.Summary.Diffs.Any(diff => !string.IsNullOrEmpty(diff.Patch)))
        return message;

      // Create new Summary with cleaned diffs
      var cleanedDiffs = message.Summary.Diffs
        .Select(diff =>
        {
          if (!string.IsNullOrEmpty(diff.Patch))
          {
            return new SnapshotFileDiff
            {
              File = diff.File,
              Patch = null,  // Strip the patch
              Additions = diff.Additions,
              Deletions = diff.Deletions,
              Status = diff.Status
            };
          }
          return diff;
        })
        .ToList();

      // Return new UserMessage with cleaned summary
      return new UserMessage
      {
        Id = message.Id,
        SessionID = message.SessionID,
        Role = message.Role,
        Time = message.Time,
        Format = message.Format,
        Summary = new Summary2
        {
          Title = message.Summary.Title,
          Body = message.Summary.Body,
          Diffs = cleanedDiffs
        },
        Agent = message.Agent,
        Model = message.Model,
        System = message.System,
        Tools = message.Tools,
        EditorContext = message.EditorContext
      };
    }

    /// <summary>
    /// Overload for AssistantMessage - returns as-is since AssistantMessage doesn't have Summary.
    /// </summary>
    public static AssistantMessage SlimInfo(AssistantMessage message)
    {
      // AssistantMessage doesn't have Summary, so no transformation needed
      return message;
    }

    /// <summary>
    /// Generic overload that works with either UserMessage or AssistantMessage.
    /// Returns UserMessage with stripped patches, or AssistantMessage unchanged.
    /// </summary>
    public static ApiClient.Message SlimInfo(ApiClient.Message message)
    {
      if (message == null)
        return null;

      if (message is UserMessage userMsg)
        return SlimInfo(userMsg);

      if (message is AssistantMessage assistantMsg)
        return SlimInfo(assistantMsg);

      return message;
    }

    /// <summary>
    /// Transforms an Anonymous6 message by slimming the info (stripping summary patches)
    /// and slimming the parts (stripping heavy metadata from tool/reasoning parts).
    /// Matches VS Code's message transformation pattern for webview transmission.
    /// </summary>
    public static WebviewMessage SlimMessage(Anonymous6 message)
    {
      if (message == null)
        return null;

      // Slim the info (UserMessage or AssistantMessage)
      var slimmedInfo = SlimInfo(message.Info);

      // Slim the parts
      var slimmedParts = SlimParts(message.Parts);

      // Return new Anonymous6 with slimmed data
      WebviewMessage result = EntityConverter.Convert(slimmedInfo);
      result.Parts = slimmedParts;
      result.CreatedAt = DateTimeOffset.Now.ToString("O");

      var time = slimmedInfo.AdditionalProperties["time"];
      if (IsObj(time))
      {
        var created = (long?)((JObject)time)["created"];
        if (created.HasValue) result.CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(created.Value).ToString("O");
      }
      return result;
    }

    /// <summary>
    /// Strips heavy metadata from an array of parts.
    /// For tool parts: strips patches, before/after content, large outputs based on tool type.
    /// For reasoning parts: strips encrypted provider metadata.
    /// For other parts: returns unchanged.
    /// </summary>
    public static System.Collections.Generic.ICollection<ApiClient.Part> SlimParts(
      System.Collections.Generic.ICollection<ApiClient.Part> parts)
    {
      if (parts == null || parts.Count == 0)
        return parts;

      var result = new System.Collections.Generic.List<ApiClient.Part>();
      foreach (var part in parts)
      {
        result.Add(SlimPart(part));
      }
      return result;
    }

    /// <summary>
    /// Strips heavy metadata from a single part.
    /// </summary>
    private static ApiClient.Part SlimPart(ApiClient.Part part)
    {
      if (part == null)
        return null;

      // Handle reasoning parts - strip encrypted metadata
      if (part is ReasoningPart reasoningPart)
      {
        return SlimReasoningPart(reasoningPart);
      }

      // Only tool parts need slimming
      if (!(part is ToolPart))
      {
        return part;
      }

      var toolPart = (ToolPart)part;
      // Get the slimmer function for this tool type
      var slimmer = GetToolSlimmer(toolPart.Tool);
      if (slimmer == null)
      {
        return toolPart;
      }

      // Apply the slimmer to the part's state
      var state = slimmer(toolPart.State);

      return new ToolPart
      {
        CallID = toolPart.CallID,
        Id = toolPart.Id,
        MessageID = toolPart.MessageID,
        Metadata = toolPart.Metadata,
        SessionID = toolPart.SessionID,
        State = state,
        Tool = toolPart.Tool
      };
    }

    /// <summary>
    /// Gets the slimmer function for a specific tool type.
    /// </summary>
    private static Func<ToolState, ToolState> GetToolSlimmer(string tool)
    {
      return tool switch
      {
        "edit" => SlimEdit,
        "apply_patch" => SlimApplyPatch,
        "multiedit" => SlimMultiedit,
        "write" => SlimWrite,
        "read" => SlimOutput,
        "glob" => SlimOutput,
        "grep" => SlimOutput,
        "list" => SlimOutput,
        "bash" => SlimBash,
        _ => null
      };
    }

    /// <summary>
    /// Strips reasoning part metadata - removes encrypted provider content.
    /// </summary>
    private static ReasoningPart SlimReasoningPart(ReasoningPart part)
    {
      var meta = part.Metadata;
      if (!IsObj(meta)) return part;
      var metaObj = (JObject)meta;
      var openai = metaObj["openai"];
      if (!IsObj(openai) || !(((JObject)openai).ContainsKey("reasoningEncryptedContent"))) return part;
      var openaiObj = (JObject)openai;
      var next = openaiObj.DeepClone();
      next["reasoningEncryptedContent"]?.Remove();

      var newMetadata = metaObj.DeepClone();
      newMetadata["openai"] = next;

      return new ReasoningPart
      {
        Id = part.Id,
        MessageID = part.MessageID,
        Metadata = newMetadata,
        SessionID = part.SessionID,
        Text = part.Text,
        Time = part.Time,
      };
    }


    // Constants for truncation limits
    private const int OUTPUT_CAP = 4000;
    private const int PATCH_CAP = 64000;

    /// <summary>
    /// Helper: check if value is a non-null object (not array).
    /// </summary>
    private static bool IsObj(object? v)
    {
      return v is JObject;
    }

    /// <summary>
    /// Helper: truncate a string to cap, appending marker when trimmed.
    /// </summary>
    private static string? Cap(string? v, int limit = OUTPUT_CAP)
    {
      if (v == null) return null;
      if (v.Length <= limit) return v;
      return v.Substring(0, limit) + "\n… (truncated, " + (v.Length - limit) + " chars omitted)";
    }

    /// <summary>
    /// Helper: return patch if within cap, otherwise null.
    /// </summary>
    private static string? Patch(string? v)
    {
      if (v == null) return null;
      if (v.Length > PATCH_CAP) return null;
      return v;
    }

    /// <summary>
    /// Helper: create object with patch field if patch is within cap.
    /// </summary>
    private static JObject? WithPatch(string? v)
    {
      var kept = Patch(v);
      return kept != null ? new JObject { ["patch"] = kept } : null;
    }

    /// <summary>
    /// edit: strip filediff.before/after while preserving bounded patches.
    /// </summary>
    private static ToolState SlimEdit(ToolState state)
    {
      var next = new ToolState
      {
        AdditionalProperties = new Dictionary<string, object>(state.AdditionalProperties)
      };
      var meta = state.AdditionalProperties["metadata"];

      if (!IsObj(meta))
      {
        next.AdditionalProperties.Remove("metadata");
        return next;
      }

      var metaObj = (JObject)meta;
      var result = new JObject();
      var fd = metaObj["filediff"];

      if (IsObj(fd))
      {
        var fdObj = (JObject)fd;
        var filediff = new JObject();

        if (fdObj["file"] is JValue fileVal && fileVal.Value is string)
          filediff["file"] = fdObj["file"];

        var patchObj = WithPatch(fdObj["patch"]?.ToString());
        if (patchObj != null)
          filediff.Merge(patchObj);

        if (fdObj["additions"] is JValue additionsVal)
          filediff["additions"] = additionsVal;
        else
          filediff["additions"] = 0;

        if (fdObj["deletions"] is JValue deletionsVal)
          filediff["deletions"] = deletionsVal;
        else
          filediff["deletions"] = 0;

        result["filediff"] = filediff;
      }

      if (metaObj["diagnostics"] != null)
        result["diagnostics"] = metaObj["diagnostics"];

      next.AdditionalProperties["metadata"] = result;

      return next;
    }

    /// <summary>
    /// apply_patch: strip full file contents and input patch text.
    /// </summary>
    private static ToolState SlimApplyPatch(ToolState state)
    {
      var next = new ToolState
      {
        AdditionalProperties = new Dictionary<string, object>(state.AdditionalProperties)
      };
      var meta = state.AdditionalProperties["metadata"];

      if (IsObj(meta))
      {
        var metaObj = (JObject)meta;
        var slim = new JObject();

        if (metaObj["diagnostics"] != null)
          slim["diagnostics"] = metaObj["diagnostics"];

        var files = metaObj["files"] as JArray;
        if (files != null)
        {
          var slimFiles = new JArray();
          foreach (var f in files)
          {
            if (f is JObject file)
            {
              var diff = Patch(file["patch"]?.ToString()) ?? Patch(file["diff"]?.ToString());
              var slimFile = new JObject();

              if (file["filePath"] != null) slimFile["filePath"] = file["filePath"];
              if (file["relativePath"] != null) slimFile["relativePath"] = file["relativePath"];
              if (file["type"] != null) slimFile["type"] = file["type"];

              var patchObj = WithPatch(diff);
              if (patchObj != null)
                slimFile.Merge(patchObj);

              if (file["additions"] != null) slimFile["additions"] = file["additions"];
              if (file["deletions"] != null) slimFile["deletions"] = file["deletions"];
              if (file["movePath"] != null) slimFile["movePath"] = file["movePath"];

              slimFiles.Add(slimFile);
            }
          }
          slim["files"] = slimFiles;
        }

        next.AdditionalProperties["metadata"] = slim;
      }

      // Strip patchText from input
      var input = state.AdditionalProperties["input"];
      if (IsObj(input))
      {
        var inputObj = (JObject)input;
        var nextInput = new JObject();
        foreach (var prop in inputObj.Properties())
        {
          if (prop.Name != "patchText")
            nextInput[prop.Name] = prop.Value;
        }
        next.AdditionalProperties["input"] = nextInput;
      }
      return next;
    }

    /// <summary>
    /// multiedit: strip nested results and top-level diff.
    /// </summary>
    private static ToolState SlimMultiedit(ToolState state)
    {
      var next = new ToolState
      {
        AdditionalProperties = new Dictionary<string, object>(state.AdditionalProperties)
      };
      var meta = state.AdditionalProperties["metadata"];

      if (IsObj(meta))
      {
        var metaObj = (JObject)meta;
        var slim = new JObject();

        if (metaObj["diagnostics"] != null)
          slim["diagnostics"] = metaObj["diagnostics"];

        var results = metaObj["results"] as JArray;
        if (results != null)
        {
          var slimResults = new JArray();
          foreach (var r in results)
          {
            if (r is JObject result)
            {
              var rs = new JObject();

              if (result["diagnostics"] != null)
                rs["diagnostics"] = result["diagnostics"];

              var fd = result["filediff"];
              if (IsObj(fd))
              {
                var fdObj = (JObject)fd;
                var filediff = new JObject();

                if (fdObj["file"] is JValue fileVal && fileVal.Value is string)
                  filediff["file"] = fdObj["file"];

                var patchObj = WithPatch(fdObj["patch"]?.ToString());
                if (patchObj != null)
                  filediff.Merge(patchObj);

                if (fdObj["additions"] is JValue additionsVal)
                  filediff["additions"] = additionsVal;
                else
                  filediff["additions"] = 0;

                if (fdObj["deletions"] is JValue deletionsVal)
                  filediff["deletions"] = deletionsVal;
                else
                  filediff["deletions"] = 0;

                rs["filediff"] = filediff;
              }

              slimResults.Add(rs);
            }
          }
          slim["results"] = slimResults;
        }

        next.AdditionalProperties["metadata"] = slim;
      }

      return next;
    }

    /// <summary>
    /// write: strip input.content, raw diff text, and filediff.before/after.
    /// </summary>
    private static ToolState SlimWrite(ToolState state)
    {
      var next = new ToolState
      {
        AdditionalProperties = new Dictionary<string, object>(state.AdditionalProperties)
      };
      var input = state.AdditionalProperties["input"];

      if (IsObj(input))
      {
        var inputObj = (JObject)input;
        var nextInput = new JObject();
        foreach (var prop in inputObj.Properties())
        {
          if (prop.Name != "content")
            nextInput[prop.Name] = prop.Value;
        }
        next.AdditionalProperties["input"] = nextInput;
      }

      var meta = state.AdditionalProperties["metadata"];
      if (IsObj(meta))
      {
        var metaObj = (JObject)meta;
        var slim = new JObject();

        if (metaObj["filepath"] != null) slim["filepath"] = metaObj["filepath"];
        if (metaObj["exists"] != null) slim["exists"] = metaObj["exists"];
        if (metaObj["diagnostics"] != null) slim["diagnostics"] = metaObj["diagnostics"];

        var fd = metaObj["filediff"];
        if (IsObj(fd))
        {
          var fdObj = (JObject)fd;
          var filediff = new JObject();

          if (fdObj["file"] is JValue fileVal && fileVal.Value is string)
            filediff["file"] = fdObj["file"];

          var patchObj = WithPatch(fdObj["patch"]?.ToString());
          if (patchObj != null)
            filediff.Merge(patchObj);

          if (fdObj["additions"] is JValue additionsVal)
            filediff["additions"] = additionsVal;
          else
            filediff["additions"] = 0;

          if (fdObj["deletions"] is JValue deletionsVal)
            filediff["deletions"] = deletionsVal;
          else
            filediff["deletions"] = 0;

          slim["filediff"] = filediff;
        }

        next.AdditionalProperties["metadata"] = slim;
      }


      return next;
    }

    /// <summary>
    /// read/list/search/glob/grep: truncate output fields.
    /// </summary>
    private static ToolState SlimOutput(ToolState state)
    {
      var next = new ToolState
      {
        AdditionalProperties = new Dictionary<string, object>(state.AdditionalProperties)
      };
      var output = state.AdditionalProperties["output"];

      if (output is JValue outputVal && outputVal.Value is string outputStr)
      {
        next.AdditionalProperties["output"] = Cap(outputStr);
      }
      else
      {
        next.AdditionalProperties["output"] = output;
      }

      return next;
    }

    /// <summary>
    /// bash: truncate metadata.output and state.output.
    /// </summary>
    private static ToolState SlimBash(ToolState state)
    {
      var next = SlimOutput(state);

      var meta = state.AdditionalProperties["metadata"];
      if (IsObj(meta))
      {
        var metaObj = (JObject)meta;
        var output = metaObj["output"];
        if (output is JValue outputVal && outputVal.Value is string outputStr)
        {
          var nextMeta = new JObject();
          foreach (var prop in metaObj.Properties())
          {
            if (prop.Name == "output")
              nextMeta[prop.Name] = Cap(outputStr);
            else
              nextMeta[prop.Name] = prop.Value;
          }
          next.AdditionalProperties["metadata"] = nextMeta;
        }
      }

      return next;
    }

  }
}
