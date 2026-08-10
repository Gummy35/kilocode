using System;
using System.Linq;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    public class DirectoryTrackingTests
    {
        [Fact]
        public void TrackDirectory_FirstCall_SetsRootDirectory()
        {
            var service = CreateService();
            
            service.TrackDirectory("C:\\test\\first");
            
            var knownDirs = service.GetKnownDirectories();
            Assert.Contains("C:\\test\\first", knownDirs);
        }

        [Fact]
        public void TrackDirectory_SubsequentCalls_DoesNotChangeRootDirectory()
        {
            var service = CreateService();
            
            service.TrackDirectory("C:\\test\\first");
            service.TrackDirectory("C:\\test\\second");
            service.TrackDirectory("C:\\test\\third");
            
            var knownDirs = service.GetKnownDirectories();
            Assert.Equal(2, knownDirs.Length);
            Assert.Contains("C:\\test\\first", knownDirs);
            Assert.Contains("C:\\test\\third", knownDirs);
        }

        [Fact]
        public void TrackDirectory_AlwaysUpdatesCurrentDirectory()
        {
            var service = CreateService();
            
            service.TrackDirectory("C:\\test\\first");
            service.TrackDirectory("C:\\test\\second");
            service.TrackDirectory("C:\\test\\third");
            
            var knownDirs = service.GetKnownDirectories();
            Assert.Contains("C:\\test\\third", knownDirs);
        }

        [Fact]
        public void TrackDirectory_NullOrEmpty_DoesNotThrow()
        {
            var service = CreateService();
            
            service.TrackDirectory(null!);
            service.TrackDirectory("");
            service.TrackDirectory("C:\\test\\valid");
            
            var knownDirs = service.GetKnownDirectories();
            Assert.Contains("C:\\test\\valid", knownDirs);
            Assert.Equal(1, knownDirs.Length);
        }

        [Fact]
        public void GetKnownDirectories_DeduplicatesRootAndCurrentWhenSame()
        {
            var service = CreateService();
            
            service.TrackDirectory("C:\\test\\same");
            
            var knownDirs = service.GetKnownDirectories();
            Assert.Equal(1, knownDirs.Length);
        }

        [Fact]
        public void RegisterDirectoryProvider_ProviderDirectoriesIncluded()
        {
            var service = CreateService();
            
            service.TrackDirectory("C:\\test\\tracked");
            
            var unsubscribe = service.RegisterDirectoryProvider(() => new[] { "C:\\test\\provider1", "C:\\test\\provider2" });
            
            var knownDirs = service.GetKnownDirectories();
            Assert.Equal(3, knownDirs.Length);
            Assert.Contains("C:\\test\\tracked", knownDirs);
            Assert.Contains("C:\\test\\provider1", knownDirs);
            Assert.Contains("C:\\test\\provider2", knownDirs);
        }

        [Fact]
        public void RegisterDirectoryProvider_UnsubscribeRemovesDirectories()
        {
            var service = CreateService();
            
            var unsubscribe = service.RegisterDirectoryProvider(() => new[] { "C:\\test\\provider" });
            unsubscribe();
            
            var knownDirs = service.GetKnownDirectories();
            Assert.DoesNotContain("C:\\test\\provider", knownDirs);
        }

        [Fact]
        public void RegisterDirectoryProvider_MultipleProviders_AllIncluded()
        {
            var service = CreateService();
            
            service.RegisterDirectoryProvider(() => new[] { "C:\\test\\provider1" });
            service.RegisterDirectoryProvider(() => new[] { "C:\\test\\provider2" });
            service.RegisterDirectoryProvider(() => new[] { "C:\\test\\provider3" });
            
            var knownDirs = service.GetKnownDirectories();
            Assert.Equal(3, knownDirs.Length);
            Assert.Contains("C:\\test\\provider1", knownDirs);
            Assert.Contains("C:\\test\\provider2", knownDirs);
            Assert.Contains("C:\\test\\provider3", knownDirs);
        }

        [Fact]
        public void RegisterDirectoryProvider_NullEmptyProviderResults_Ignored()
        {
            var service = CreateService();
            
            service.RegisterDirectoryProvider(() => new[] { "C:\\test\\valid", null!, "" });
            service.RegisterDirectoryProvider(() => System.Array.Empty<string>());
            service.RegisterDirectoryProvider(() => null!);
            
            var knownDirs = service.GetKnownDirectories();
            Assert.Equal(1, knownDirs.Length);
            Assert.Contains("C:\\test\\valid", knownDirs);
        }

        [Fact]
        public void RegisterDirectoryProvider_ProviderException_LoggedButContinues()
        {
            var service = CreateService();
            
            service.RegisterDirectoryProvider(() => { throw new InvalidOperationException("Test exception"); });
            service.RegisterDirectoryProvider(() => new[] { "C:\\test\\valid" });
            
            var knownDirs = service.GetKnownDirectories();
            Assert.Contains("C:\\test\\valid", knownDirs);
            Assert.Equal(1, knownDirs.Length);
        }

        [Fact]
        public void RegisterDirectoryProvider_ProviderNotInvokedWhileHoldingLock()
        {
            var service = CreateService();
            
            var providerCanTrackDirectory = false;
            
            service.RegisterDirectoryProvider(() =>
            {
                // If the lock is held, this will block forever (or until timeout).
                // If the lock is released, this call succeeds immediately.
                service.TrackDirectory("C:\\test\\from-provider");
                providerCanTrackDirectory = true;
                return new[] { "C:\\test\\provider" };
            });
            
            var knownDirs = service.GetKnownDirectories();
            
            Assert.True(providerCanTrackDirectory, "Provider should be able to call TrackDirectory, proving the lock was released before provider invocation");
        }

        private KiloConnectionService CreateService()
        {
            var mockBackend = new MockBackendManager();
            return new KiloConnectionService(mockBackend);
        }

        private class MockBackendManager : CliBackendManager
        {
            public MockBackendManager() : base() { }
            public new System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken = default) => System.Threading.Tasks.Task.CompletedTask;
            public new string? BaseUrl => "http://127.0.0.1:9999";
            public new string? Password => "test-password";
            public new int? GetPort() => 9999;
        }
    }
}
