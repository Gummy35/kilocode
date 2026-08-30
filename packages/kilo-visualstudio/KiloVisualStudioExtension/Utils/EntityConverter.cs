using EnvDTE;
using KiloExtensionDTOs;
using KiloExtensionDTOs.Agents;
using KiloExtensionDTOs.ExtensionMessages;
using KiloExtensionDTOs.KiloConfig;
using KiloExtensionDTOs.Parts;
using KiloExtensionDTOs.Profile;
using KiloExtensionDTOs.Questions;
using KiloExtensionDTOs.Sessions;
using KiloVisualStudioExtension.ApiClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Navigation;
using Message = KiloVisualStudioExtension.ApiClient.Message;
using Notification = KiloVisualStudioExtension.ApiClient.Anonymous11;
using WebviewMessage = KiloExtensionDTOs.Sessions.Message;


namespace KiloVisualStudioExtension.Utils
{
  public class EntityConverter
  {

    public static KilocodeNotificationAction Convert(ApiClient.Action action)
    {
      return new KilocodeNotificationAction
      {
        ActionText = action.ActionText,
        ActionURL = action.ActionURL
      };
    }

    public static WebviewMessage Convert(UserMessage message)
    {
      return new WebviewMessage
      {
        Agent = message.Agent,
        Id = message.Id,
        Model = Convert(message.Model),
        Role = message.Role,
        SessionID = message.SessionID,
        Summary = message.Summary,
        Time = Convert(message.Time),
      };
    }

    public static WebviewMessage Convert(AssistantMessage message)
    {
      return new WebviewMessage
      {
        Agent = message.Agent,
        Cost = message.Cost,
        Error = Convert(message.Error),
        Finish = message.Finish,
        Id = message.Id,
        Mode = message.Mode,
        ModelID = message.ModelID,
        ParentID = message.ParentID,
        Path = Convert(message.Path),
        ProviderID = message.ProviderID,
        Role = message.Role,
        SessionID = message.SessionID,
        Summary = message.Summary,
        Time = Convert(message.Time),
        Tokens = Convert(message.Tokens),
      };
    }


    public static WebviewMessage Convert(Message message)
    {
      if (message is UserMessage userMessage) return Convert(userMessage);
      if (message is AssistantMessage assistantMessage) return Convert(assistantMessage);
      return null;
    }

    public static KilocodeNotification Convert(Anonymous11 notification)
    {
      return new KilocodeNotification
      {
        Action = Convert(notification.Action),
        Id = notification.Id,
        Message = notification.Message,
        ShowIn = notification.ShowIn,
        SuggestModelId = notification.SuggestModelId,
        Title = notification.Title,
      };
    }

    public static SkillInfo Convert(Anonymous3 skill)
    {
      return new SkillInfo
      {
        Description = skill.Description,
        Location = skill.Location,
        Name = skill.Name
      };
    }

    public static McpSkillCommandEnum Convert(CommandSource source)
    {
      return source switch
      {
        CommandSource.Command => McpSkillCommandEnum.Command,
        CommandSource.Mcp => McpSkillCommandEnum.Mcp,
        CommandSource.Skill => McpSkillCommandEnum.Skill
      };
    }

    public static SlashCommandInfo Convert(ApiClient.Command command)
    {
      return new SlashCommandInfo
      {
        Description= command.Description,
        Hints = command.Hints.ToList(),
        Name = command.Name,
        Source = Convert(command.Source)
      };
    }

    public static SessionInfo Convert(Session session)
    {
      return new SessionInfo
      {
        Id = session.Id,
        ParentID = string.IsNullOrWhiteSpace(session.ParentID) ? null : session.ParentID,
        Title = session.Title,
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(session.Time.Created).ToString("O"),
        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(session.Time.Updated).ToString("O"),
        Revert = session.Revert ?? null,
        Summary = session.Summary ?? null
      };
    }

    public static SessionUpdate Convert(Session2 session)
    {
      return new SessionUpdate
      {
        Id = session.Id,
        ParentID = string.IsNullOrWhiteSpace(session.ParentID) ? null : session.ParentID,
        Title = session.Title,
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds(session.Time.Created).ToString("O"),
        UpdatedAt = DateTimeOffset.FromUnixTimeMilliseconds(session.Time.Updated).ToString("O"),
        Revert = session.Revert ?? null,
        Summary = session.Summary ?? null
      };
    }

    public static AgentInfo Convert(Agent agent)
    {
      return new AgentInfo
      {
        Color = agent.Color,
        Deprecated = agent.Deprecated,
        Description = agent.Description,
        DisplayName = agent.DisplayName,
        Hidden = agent.Hidden,
        Mode = agent.Mode,
        Name = agent.Name,
        Native = agent.Native,
        Permission = agent.Permission
      };
    }

    public static ProfileData Convert(Response23 response)
    {
      return new ProfileData
      {
        Profile = Convert(response.Profile),
        Balance = Convert(response.Balance),
        KiloPass = Convert(response.KiloPass),
        CurrentOrgId = Convert(response.CurrentOrgId)
      };
    }

    public static ProfileType Convert(Profile profile)
    {
      if (profile == null) return null;
      return new ProfileType
      {
        Email = profile.Email,
        Name = profile.Name,
        Organizations = profile.Organizations?.Select(o => Convert(o)).ToList(),
        SelectedOrganizationId = profile.SelectedOrganizationId,
        HasPersonalAccount = profile.HasPersonalAccount
      };
    }

    public static OrganizationsItemType Convert(Organizations o)
    {
      if (o == null) return null;
      return new OrganizationsItemType
      {
        Id = o.Id,
        Name = o.Name,
        Role = o.Role
      };
    }

    public static string Convert(CurrentOrgId orgId)
    {
      if (orgId == null) return null;
      return orgId.Value;
    }

    public static KilocodeBalance Convert(Balance balance)
    {
      if (balance == null) return null;
      return new KilocodeBalance
      {
        Balance = balance.balance
      };
    }

    public static KiloPassState Convert(KiloPass kilopass)
    {
      if (kilopass == null) return null;
      return new KiloPassState
      {
        CurrentPeriodBaseCreditsUsd = kilopass.CurrentPeriodBaseCreditsUsd,
        CurrentPeriodBonusCreditsUsd = kilopass.CurrentPeriodBonusCreditsUsd,
        CurrentPeriodUsageUsd = kilopass.CurrentPeriodUsageUsd,
        NextBillingAt = kilopass.NextBillingAt
      };
    }

    public static SkillsConfig Convert(Skills skills)
    {
      if (skills == null) return null;
      return new SkillsConfig
      {
        Paths = skills.Paths,
        Urls = skills.Urls
      };
    }

    public static ToolType Convert(QuestionTool tool)
    {
      return new ToolType
      {
        CallID = tool.CallID,
        MessageID = tool.MessageID
      };
    }

    public static ToolType Convert(Tool3 tool)
    {
      return new ToolType
      {
        CallID = tool.CallID,
        MessageID = tool.MessageID
      };
    }

    public static ToolType Convert(Tool tool)
    {
      return new ToolType
      {
        CallID = tool.CallID,
        MessageID = tool.MessageID
      };
    }

    public static List<KiloExtensionDTOs.Questions.QuestionOption> Convert(ICollection<ApiClient.QuestionOption> options)
    {
      return options.Select(option => new KiloExtensionDTOs.Questions.QuestionOption
      {
        Description = option.Description,
        DescriptionKey = option.DescriptionKey,
        Label = option.Label,
        LabelKey = option.LabelKey,
        Mode = option.Mode
      }).ToList();
    }

    public static List<SuggestionAction> Convert(ICollection<ApiClient.Actions> actions)
    {
      return actions.Select(action => new SuggestionAction
      {
        Description = action.Description,
        Label = action.Label,
        Prompt = action.Prompt
      }).ToList();
    }

    public static List<KiloExtensionDTOs.Questions.QuestionInfo> Convert(ICollection<ApiClient.QuestionInfo> questions)
    {
      return questions.Select(question => new KiloExtensionDTOs.Questions.QuestionInfo
      {
        Custom = question.Custom,
        Header = question.Header,
        HeaderKey = question.HeaderKey,
        Multiple = question.Multiple,
        Options = Convert(question.Options),
        Question = question.Question,
        QuestionKey = question.QuestionKey
      }).ToList();
    }

    public static List<TodoItem> Convert(ICollection<Todo> todos)
    {
      return todos.Select(todo => new TodoItem
      {
        Content = todo.Content,
        Status = todo.Status,
      }).ToList(); 
    }

    public static SessionCloseReasonEnum Convert(Properties15Reason reason)
    {
      return reason switch
      {
        Properties15Reason.Interrupted => SessionCloseReasonEnum.Interrupted,
        Properties15Reason.Completed => SessionCloseReasonEnum.Completed,
        Properties15Reason.Error => SessionCloseReasonEnum.Error
      };
    }

    public static KiloExtensionDTOs.DisabledManualAutoEnum Convert(ConfigShare c)
    {
      switch (c)
      {
        case ConfigShare.Manual:
          return KiloExtensionDTOs.DisabledManualAutoEnum.Manual;
        case ConfigShare.Disabled:
          return KiloExtensionDTOs.DisabledManualAutoEnum.Disabled;
        default:
          return KiloExtensionDTOs.DisabledManualAutoEnum.Auto;
      }
    }


    public static WatcherConfig Convert(Watcher w)
    {
      if (w == null) return null;
      return new WatcherConfig { Ignore = w.Ignore };
    }

    public static CompactionConfig Convert(Compaction c)
    {
      if (c == null) return null;
      return new CompactionConfig
      {
        Auto = c.Auto,
        Prune = c.Prune,
        //        Threshold_percent = c.Threshold_percent
      };
    }

    public static CommitMessageConfig Convert(Commit_message c)
    {
      if (c == null) return null;
      return new CommitMessageConfig { Prompt = c.Prompt };
    }

    public static ExperimentalConfig Convert(Experimental experimental)
    {
      if (experimental == null) return null;
      return new ExperimentalConfig
      {
        Agent_requirements = experimental.Agent_requirements,
        Batch_tool = experimental.Batch_tool,
        Codebase_search = experimental.Codebase_search,
        Continue_loop_on_deny = experimental.Continue_loop_on_deny,
        Image_generation = experimental.Image_generation,
        Image_generation_model = experimental.Image_generation_model,
        Mcp_timeout = experimental.Mcp_timeout,
        Native_notebook_tools = experimental.Native_notebook_tools,
        Primary_tools = experimental.Primary_tools,
        Speech_to_text_model = experimental.Speech_to_text_model,
        Swe_pruner = experimental.Swe_pruner,
        Swe_pruner_model = experimental.Swe_pruner_model
      };
    }

    public static SandboxConfig Convert(Sandbox sandbox)
    {
      if (sandbox == null) return null;
      return new SandboxConfig
      {
        Allowed_hosts = sandbox.Allowed_hosts,
        Enabled = sandbox.Enabled,
        Network = sandbox.Network == SandboxNetwork.Allow ? AllowDenyEnum.Allow : AllowDenyEnum.Deny,
        Writable_paths = sandbox.Writable_paths
      };
    }

    public static IndexingProviderEnum Convert(IndexingConfigProvider provider)
    {
      switch (provider)
      {
        case IndexingConfigProvider.Kilo: return IndexingProviderEnum.Kilo;
        case IndexingConfigProvider.Openai: return IndexingProviderEnum.Openai;
        case IndexingConfigProvider.Ollama: return IndexingProviderEnum.Ollama;
        case IndexingConfigProvider.OpenaiCompatible: return IndexingProviderEnum.OpenaiCompatible;
        case IndexingConfigProvider.Gemini: return IndexingProviderEnum.Gemini;
        case IndexingConfigProvider.Mistral: return IndexingProviderEnum.Mistral;
        case IndexingConfigProvider.VercelAiGateway: return IndexingProviderEnum.VercelAiGateway;
        case IndexingConfigProvider.Bedrock: return IndexingProviderEnum.Bedrock;
        case IndexingConfigProvider.Openrouter: return IndexingProviderEnum.Openrouter;
        case IndexingConfigProvider.Voyage: return IndexingProviderEnum.Voyage;
        default: return IndexingProviderEnum.Kilo;
      }
    }

    public static KiloExtensionDTOs.KiloConfig.IndexingConfig Convert(ApiClient.IndexingConfig config)
    {
      return new KiloExtensionDTOs.KiloConfig.IndexingConfig
      {
        Bedrock = config.Bedrock == null ? null : new BedrockType { Profile = config.Bedrock.Profile, Region = config.Bedrock.Region },
        //      Dimension = config.Dimension,
        EmbeddingBatchSize = config.EmbeddingBatchSize,
        Enabled = config.Enabled,
        FileExtensions = config.FileExtensions,
        Gemini = new GeminiType { ApiKey = config.Gemini.ApiKey },
        Kilo = config.Kilo == null ? null : new KiloType { ApiKey = config.Kilo.ApiKey, BaseUrl = config.Kilo.BaseUrl, OrganizationId = config.Kilo.OrganizationId },
        Lancedb = config.Lancedb == null ? null : new LancedbType { Directory = config.Lancedb.Directory },
        Mistral = config.Mistral == null ? null : new MistralType { ApiKey = config.Mistral.ApiKey },
        //        Model = config.Model,
        Ollama = config.Ollama == null ? null : new OllamaType { BaseUrl = config.Ollama.BaseUrl },
        Openai = config.Openai == null ? null : new OpenaiType { ApiKey = config.Openai.ApiKey },
        OpenaiCompatible = config.OpenaiCompatible == null ? null : new OpenaiCompatibleType { ApiKey = config.OpenaiCompatible.ApiKey, BaseUrl = config.OpenaiCompatible.BaseUrl },
        Openrouter = config.Openrouter == null ? null : new OpenrouterType { ApiKey = config.Openrouter.ApiKey, SpecificProvider = config.Openrouter.SpecificProvider },
        Provider = Convert(config.Provider),
        Qdrant = config.Qdrant == null ? null : new QdrantType { ApiKey = config.Qdrant.ApiKey, Url = config.Qdrant.Url },
        ScannerMaxBatchRetries = config.ScannerMaxBatchRetries,
        SearchMaxResults = config.SearchMaxResults,
        SearchMinScore = config.SearchMinScore,
        VectorStore = config.VectorStore == IndexingConfigVectorStore.Qdrant ? LancedbQdrantEnum.Qdrant : LancedbQdrantEnum.Lancedb,
        VercelAiGateway = config.VercelAiGateway == null ? null : new VercelAiGatewayType { ApiKey = config.VercelAiGateway.ApiKey },
        Voyage = config.Voyage == null ? null : new VoyageType { ApiKey = config.Voyage.ApiKey }

      };
    }

    public static KiloExtensionDTOs.KiloConfig.Config Convert(ApiClient.Config config)
    {
      if (config == null) return null;
      return new KiloExtensionDTOs.KiloConfig.Config
      {
        Agent = config.Agent,
        Model = config.Model,
        Small_model = config.Small_model,
        Subagent_model = config.Subagent_model,
        Subagent_variant = config.Subagent_variant,
        Subagent_variant_overrides = config.Subagent_variant_overrides,
        Default_agent = config.Default_agent,
        Provider = config.Provider,
        Disabled_providers = config.Disabled_providers,
        Enabled_providers = config.Enabled_providers,
        Mcp = config.Mcp,
        Command = config.Command,
        Instructions = config.Instructions,
        Skills = Convert(config.Skills),
        Snapshot = config.Snapshot,
        Remote_control = config.Remote_control,
        Terminal_command_display = config.Terminal_command_display == ConfigTerminal_command_display.Expanded ? TerminalCommandDisplayEnum.Expanded : TerminalCommandDisplayEnum.Collapsed,
        Code_edit_display = config.Code_edit_display == ConfigCode_edit_display.Expanded ? CodeEditDisplayEnum.Expanded : CodeEditDisplayEnum.Collapsed,
        Hide_prompt_training_models = config.Hide_prompt_training_models,
        Share = Convert(config.Share),
        Username = config.Username,
        Watcher = Convert(config.Watcher),
        Formatter = config.Formatter,
        Lsp = config.Lsp,
        Compaction = Convert(config.Compaction),
        Commit_message = Convert(config.Commit_message),
        Tools = config.Tools,
        Auto_collapse_reasoning = config.Auto_collapse_reasoning,
        Experimental = Convert(config.Experimental),
        Sandbox = Convert(config.Sandbox),
        Indexing = Convert(config.Indexing),
        Permission = config.Permission
      };
    }
    public static TimeType Convert(Time5 time)
    {
      return new TimeType { Created = time.Created };
    }
    public static TimeType Convert(Time6 time)
    {
      return new TimeType { Created = time.Created, Completed = time.Completed };
    }

    public static ModelType Convert(Model3 model)
    {
      return new ModelType { ModelID = model.ModelID, ProviderID = model.ProviderID, Variant = model.Variant };
    }

    public static CacheType Convert(Cache2 cache)
    {
      return new CacheType { Read = cache.Read, Write = cache.Write };
    }

    public static TokenUsage Convert(Tokens2 tokens)
    {
      return new TokenUsage { Cache = Convert(tokens.Cache), Input = tokens.Input, Output = tokens.Output, Reasoning = tokens.Reasoning};
    }
    public static ErrorType Convert(Error error)
    {
      return new ErrorType { Data = error.Data, Name = error.Name };
    }

    public static PathType Convert(Path2 path)
    {
      return new PathType { Cwd = path.Cwd, Root = path.Root };
    }
  }

  public static class MessageConverter
  {
    public static KiloExtensionDTOs.Sessions.Message Convert(UserMessage message)
    {
      return new KiloExtensionDTOs.Sessions.Message
      {
        Agent = message.Agent,
        Content = null,
        Cost = null,
        Error = null,
        Finish = null,
        Id = message.Id,
        Mode = null,
        Model = EntityConverter.Convert(message.Model),
        ModelID = null,
        ParentID = null,
        Parts = null,
        Path = null,
        ProviderID = null,
        Role = message.Role,
        SessionID = message.SessionID,
        Summary = message.Summary,
        Time = EntityConverter.Convert(message.Time),
        Tokens = null,
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)message.Time.Created).ToString("O"),
      };
    }

    public static KiloExtensionDTOs.Sessions.Message Convert(AssistantMessage message)
    {
      return new KiloExtensionDTOs.Sessions.Message
      {
        Agent = message.Agent,
        Content = null,
        Cost = message.Cost,
        Error = EntityConverter.Convert(message.Error),
        Finish = message.Finish,
        Id = message.Id,
        Mode = message.Mode,
        Model = null,
        ModelID = message.ModelID,
        ParentID = message.ParentID,
        Parts = null,
        Path = EntityConverter.Convert(message.Path),
        ProviderID = message.ProviderID,
        Role = message.Role,
        SessionID = message.SessionID,
        Summary = message.Summary,
        Time = EntityConverter.Convert(message.Time),
        Tokens = EntityConverter.Convert(message.Tokens),
        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)message.Time.Created).ToString("O"),
      };
    }

    public static KiloExtensionDTOs.Sessions.Message Convert(ApiClient.Message message)
    {
      if (message is AssistantMessage) return Convert((AssistantMessage)message);
      if (message is UserMessage) return Convert((UserMessage)message);
      return null;
    }

  }
}
