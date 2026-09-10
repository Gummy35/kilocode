using EnvDTE;
using KiloExtensionDTOs;
using KiloExtensionDTOs.Parts;
using KiloExtensionDTOs.WebviewMessages;
using KiloVisualStudioExtension.ApiClient;
using KiloVisualStudioExtension.Utils;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;
using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace KiloVisualStudioExtension.Services.Handlers.Ui
{
    /// <summary>
    /// Handles UI and panel operations like open settings panel, open sub-agent viewer, reload, save image.
    /// This matches the VS Code pattern where UI operations are extracted into separate handler modules.
    /// </summary>
    public class UiService : ServiceProviderServiceBase
  {
        private bool _disposed;

        private VSProvider Provider => _serviceProvider.GetService<VSProvider>() 
            ?? throw new InvalidOperationException("VSProvider not registered in service provider");

        /// <summary>
        /// Creates a new UiHandlerService instance.
        /// </summary>
        /// <param name="serviceProvider">The service provider for dependency injection.</param>
        public UiService(ServiceProvider serviceProvider):base(serviceProvider)
        {
        }

    public async Task<string?> ShowSaveFileDialogAsync(string defaultPath, string[] extensions, string filter = "All files (*.*)|*.*")
    {
      await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

      var dialog = new SaveFileDialog
      {
        FileName = System.IO.Path.GetFileName(defaultPath),
        DefaultExt = extensions.Length > 0 ? extensions[0] : "",
        Filter = filter,
        InitialDirectory = System.IO.Path.GetDirectoryName(defaultPath) ?? Environment.CurrentDirectory
      };

      var result = dialog.ShowDialog();
      return result == true ? dialog.FileName : null;
    }

    public void ShowMessage(string message, string caption = "")
    {
      MessageBox.Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);
    }






    /// <summary>
    /// Handles the openSettingsPanel message from the webview.
    /// Opens the settings panel with the specified tab.
    /// </summary>
    /// <param name="payload">The message payload containing tab name.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task HandleOpenSettingsPanelAsync(JsonElement? payload)
        {
            System.Diagnostics.Debug.WriteLine("[Kilo] UiHandler: openSettingsPanel");
            string? tab = null;
            if (payload.HasValue && payload.Value.TryGetProperty("tab", out var tabProp))
            {
                tab = tabProp.GetString();
            }
            var navigateMsg = new { type = "navigate", view = "settings", tab };
            Provider.PostMessage(JsonSerializer.Serialize(navigateMsg));
        }

        /// <summary>
        /// Handles the settingsTabChanged message from the webview.
        /// Notifies when the settings tab changes.
        /// </summary>
        /// <param name="payload">The message payload.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public void HandleSettingsTabChanged(JsonElement? payload)
        {
            if (payload.HasValue && payload.Value.TryGetProperty("tab", out var tabProp))
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] UiHandler: settingsTabChanged - tab={tabProp.GetString()}");
            }
        }

        /// <summary>
        /// Handles the openSubAgentViewer message from the webview.
        /// Opens the sub-agent viewer for a specific session.
        /// </summary>
        /// <param name="payload">The message payload containing session ID.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleOpenSubAgentViewerAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await Provider.SendErrorAsync("Missing payload", "Open sub-agent viewer payload is required");
                return;
            }

            try
            {
                string? sessionID = null;
                if (payload.Value.TryGetProperty("sessionID", out var sidProp))
                {
                    sessionID = sidProp.GetString();
                }
                
                string? title = null;
                if (payload.Value.TryGetProperty("title", out var titleProp))
                {
                    title = titleProp.GetString();
                }

                System.Diagnostics.Debug.WriteLine($"[Kilo] UiHandler: Open sub-agent viewer - session: {sessionID}");
                Provider.PostMessage(JsonSerializer.Serialize(new { type = "subAgentViewerOpened", sessionID, title }));
            }
            catch (Exception ex)
            {
                await Provider.SendErrorAsync("Open sub-agent viewer error", ex.Message);
            }
        }

        /// <summary>
        /// Handles the reload message from the webview.
        /// Reloads config, skills, agents, and commands from disk by rebooting the backend instance.
        /// Matches VS Code's handleReload pattern - calls /instance/reload endpoint.
        /// </summary>
        /// <param name="payload">The message payload (unused).</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleReloadAsync(JsonElement? payload)
        {
            var nswagClient = Provider.GetNswagClient();
            if (nswagClient == null)
            {
                System.Diagnostics.Debug.WriteLine("[Kilo] UiHandler: reload - no NSwag client");
                return;
            }

            var directory = System.Environment.CurrentDirectory;
            
            try
            {
                await nswagClient.Instance_reloadAsync(directory, "");
                System.Diagnostics.Debug.WriteLine($"[Kilo] UiHandler: Backend reloaded for directory {directory}");
                
                Provider.PostMessage(JsonSerializer.Serialize(new { type = "configReloaded" }));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Kilo] UiHandler: reload failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Handles the saveImage message from the webview.
        /// Saves an image to disk.
        /// </summary>
        /// <param name="payload">The message payload containing image data.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task HandleSaveImageAsync(JsonElement? payload)
        {
            if (payload == null)
            {
                await Provider.SendErrorAsync("Missing payload", "Save image payload is required");
                return;
            }

            try
            {
                var imageData = payload.Value.TryGetProperty("imageData", out var data) ? data.GetString() : "";
                var filename = payload.Value.TryGetProperty("filename", out var fn) ? fn.GetString() : "image.png";
                
                if (string.IsNullOrEmpty(imageData))
                {
                    await Provider.SendErrorAsync("Invalid payload", "Image data is required");
                    return;
                }
                
                var bytes = Convert.FromBase64String(imageData);
                var path = System.IO.Path.Combine(System.Environment.CurrentDirectory, filename);
                System.IO.File.WriteAllBytes(path, bytes);
                System.Diagnostics.Debug.WriteLine($"[Kilo] UiHandler: Image saved: {path}");
                
                Provider.PostMessage(JsonSerializer.Serialize(new { type = "imageSaved", path }));
            }
            catch (Exception ex)
            {
                await Provider.SendErrorAsync("Save image error", ex.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
        }




    internal async Task<bool> HandleEditorOpenMessageAsync(IEditorActionMessage message)
    {      
      return await EditorActions.HandleEditorActionAsync(message, new EditorActions.Options
      {
        Dir = () => _serviceProvider.GetService<ProjectDirectoryProvider>().GetWorkspaceDirectory(Provider.GetCurrentSessionID()),
        Storage = null, //this.extensionContext?.globalStorageUri,
        Post = (msg) => Provider.PostMessage(msg)
    });
    }
  }
}

