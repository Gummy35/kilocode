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
    /// Tests for session model store - mirrors session-model-store.test.ts from VS Code
    /// </summary>
    public class SessionModelStoreTests
    {
        private class ModelSelection
        {
            public string SessionID { get; set; }
            public string ProviderID { get; set; }
            public string ModelID { get; set; }
        }

        private class ModelStore
        {
            private readonly Dictionary<string, ModelSelection> _selections = new Dictionary<string, ModelSelection>();

            public void SetSelection(string sessionID, string providerID, string modelID)
            {
                _selections[sessionID] = new ModelSelection 
                { 
                    SessionID = sessionID, 
                    ProviderID = providerID, 
                    ModelID = modelID 
                };
            }

            public ModelSelection GetSelection(string sessionID)
            {
                _selections.TryGetValue(sessionID, out var selection);
                return selection;
            }

            public void ClearSelection(string sessionID)
            {
                _selections.Remove(sessionID);
            }

            public bool HasSelection(string sessionID)
            {
                return _selections.ContainsKey(sessionID);
            }
        }

        [Fact]
        public void Stores_and_retrieves_model_selection()
        {
            // Arrange
            var store = new ModelStore();

            // Act
            store.SetSelection("s1", "provider-1", "model-1");

            // Assert
            store.HasSelection("s1").Should().BeTrue();

            var selection = store.GetSelection("s1");
            selection.Should().NotBeNull();
            selection.ProviderID.Should().Be("provider-1");
            selection.ModelID.Should().Be("model-1");
        }

        [Fact]
        public void Clears_model_selection()
        {
            // Arrange
            var store = new ModelStore();
            store.SetSelection("s1", "provider-1", "model-1");

            // Act
            store.ClearSelection("s1");

            // Assert
            store.HasSelection("s1").Should().BeFalse();
            store.GetSelection("s1").Should().BeNull();
        }

        [Fact]
        public void Overwrites_existing_selection()
        {
            // Arrange
            var store = new ModelStore();
            store.SetSelection("s1", "provider-1", "model-1");

            // Act
            store.SetSelection("s1", "provider-2", "model-2");

            // Assert
            var selection = store.GetSelection("s1");
            selection.ProviderID.Should().Be("provider-2");
            selection.ModelID.Should().Be("model-2");
        }
    }
}
