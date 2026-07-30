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
    /// Tests for session preferences - mirrors session-preferences.test.ts from VS Code
    /// </summary>
    public class SessionPreferencesTests
    {
        private class Preferences
        {
            public string SessionID { get; set; }
            public string ModelID { get; set; }
            public int Temperature { get; set; }
            public bool UseSandbox { get; set; }
        }

        private class PreferenceStore
        {
            private readonly Dictionary<string, Preferences> _preferences = new Dictionary<string, Preferences>();

            public void SetPreferences(string sessionID, Preferences prefs)
            {
                _preferences[sessionID] = prefs;
            }

            public Preferences GetPreferences(string sessionID)
            {
                _preferences.TryGetValue(sessionID, out var prefs);
                return prefs;
            }

            public void ClearPreferences(string sessionID)
            {
                _preferences.Remove(sessionID);
            }

            public bool HasPreferences(string sessionID)
            {
                return _preferences.ContainsKey(sessionID);
            }
        }

        [Fact]
        public void Stores_and_retrieves_session_preferences()
        {
            // Arrange
            var store = new PreferenceStore();
            var prefs = new Preferences 
            { 
                SessionID = "s1", 
                ModelID = "model-1", 
                Temperature = 70,
                UseSandbox = true
            };

            // Act
            store.SetPreferences("s1", prefs);

            // Assert
            store.HasPreferences("s1").Should().BeTrue();

            var retrieved = store.GetPreferences("s1");
            retrieved.Should().NotBeNull();
            retrieved.ModelID.Should().Be("model-1");
            retrieved.Temperature.Should().Be(70);
            retrieved.UseSandbox.Should().BeTrue();
        }

        [Fact]
        public void Clears_session_preferences()
        {
            // Arrange
            var store = new PreferenceStore();
            store.SetPreferences("s1", new Preferences { SessionID = "s1", ModelID = "model-1" });

            // Act
            store.ClearPreferences("s1");

            // Assert
            store.HasPreferences("s1").Should().BeFalse();
            store.GetPreferences("s1").Should().BeNull();
        }

        [Fact]
        public void Overwrites_existing_preferences()
        {
            // Arrange
            var store = new PreferenceStore();
            store.SetPreferences("s1", new Preferences { SessionID = "s1", ModelID = "model-1", Temperature = 50 });

            // Act
            store.SetPreferences("s1", new Preferences { SessionID = "s1", ModelID = "model-2", Temperature = 90 });

            // Assert
            var retrieved = store.GetPreferences("s1");
            retrieved.ModelID.Should().Be("model-2");
            retrieved.Temperature.Should().Be(90);
        }
    }
}




