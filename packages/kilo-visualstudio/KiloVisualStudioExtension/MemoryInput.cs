using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Services.Handlers.Memory;

namespace KiloVisualStudioExtension
{
  public class MemoryInput : IKiloProviderMemoryInput
  {
    private readonly VSProvider _provider;

    public MemoryInput(VSProvider provider)
    {
      _provider = provider;
    }

    public KiloApiClient? Client()
    {
      return _provider.GetNswagClient();
    }

    public ApiClient.Session? Session()
    {
      // Assuming VSProvider has a _currentSessionID field
      var sessionID = _provider.GetCurrentSessionID();
      if (string.IsNullOrEmpty(sessionID))
        return null;

      // You may need to fetch the session from the API
      return new ApiClient.Session { Id = sessionID };
    }

    public string? Dir(string? sessionID = null)
    {
      // Return the workspace/project directory
      // You may need to implement directory resolution logic
      return System.Environment.CurrentDirectory;
    }

    public void Post(object message)
    {
      _provider.PostMessage(message);
    }
  }
}
