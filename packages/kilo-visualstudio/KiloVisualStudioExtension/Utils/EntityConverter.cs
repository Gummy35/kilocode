using KiloExtensionDTOs;
using KiloExtensionDTOs.KiloConfig;
using KiloExtensionDTOs.Profile;
using KiloVisualStudioExtension.ApiClient;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Navigation;
using Notification = KiloVisualStudioExtension.ApiClient.Anonymous11;

namespace KiloVisualStudioExtension.Utils
{
  public class EntityConverter
  {
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
      return new OrganizationsItemType {
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

    public static IndexingProvider Convert(IndexingConfigProvider provider)
    {
      switch (provider)
      {
        case IndexingConfigProvider.Kilo: return IndexingProvider.Kilo;
        case IndexingConfigProvider.Openai: return IndexingProvider.Openai;
        case IndexingConfigProvider.Ollama: return IndexingProvider.Ollama;
        case IndexingConfigProvider.OpenaiCompatible: return IndexingProvider.OpenaiCompatible;
        case IndexingConfigProvider.Gemini: return IndexingProvider.Gemini;
        case IndexingConfigProvider.Mistral: return IndexingProvider.Mistral;
        case IndexingConfigProvider.VercelAiGateway: return IndexingProvider.VercelAiGateway;
        case IndexingConfigProvider.Bedrock: return IndexingProvider.Bedrock;
        case IndexingConfigProvider.Openrouter: return IndexingProvider.Openrouter;
        case IndexingConfigProvider.Voyage: return IndexingProvider.Voyage;
        default: return IndexingProvider.Kilo;
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
        Voyage = config.Voyage == null ? null : new VoyageType { ApiKey = config.Voyage.ApiKey}

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
        Terminal_command_display = config.Terminal_command_display == ConfigTerminal_command_display.Expanded ? TerminalCommandDisplay.Expanded : TerminalCommandDisplay.Collapsed,
        Code_edit_display = config.Code_edit_display == ConfigCode_edit_display.Expanded ? CodeEditDisplay.Expanded : CodeEditDisplay.Collapsed,
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
  }
}
