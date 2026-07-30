using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;

namespace KiloVisualStudioExtension.Tests
{
    /// <summary>
    /// Tests for draft store - mirrors draft-store.test.ts from VS Code
    /// These tests verify prompt draft storage and management
    /// </summary>
    public class DraftStoreTests
    {
        // Draft store implementation - THIS NEEDS TO BE IMPLEMENTED IN THE VS EXTENSION
        private class DraftStore
        {
            public Dictionary<string, DraftData> Drafts { get; } = new Dictionary<string, DraftData>();
            public Dictionary<string, ReviewDraft> ReviewDrafts { get; } = new Dictionary<string, ReviewDraft>();
            public Dictionary<string, ImageDraft> ImageDrafts { get; } = new Dictionary<string, ImageDraft>();
            public Dictionary<string, int> ScrollDrafts { get; } = new Dictionary<string, int>();
            public HashSet<string> DiscardedPendingDrafts { get; } = new HashSet<string>();
            public HashSet<string> PendingSends { get; } = new HashSet<string>();

            public void SavePromptDraft(string key, string draftType, List<ReviewDraft> reviews, List<ImageDraft> images, int scroll)
            {
                Drafts[key] = new DraftData { Type = draftType };
                foreach (var review in reviews)
                {
                    ReviewDrafts[review.Id] = review;
                }
                foreach (var image in images)
                {
                    ImageDrafts[image.Filename] = image;
                }
                ScrollDrafts[key] = scroll;
            }

            public void DiscardPendingDraft(string pendingId)
            {
                var normalizedId = NormalizePendingId(pendingId);
                DiscardedPendingDrafts.Add(normalizedId);

                // Clear all associated drafts
                var keysToRemove = new List<string>();
                foreach (var key in Drafts.Keys)
                {
                    if (key.Contains(normalizedId)) keysToRemove.Add(key);
                }
                foreach (var key in keysToRemove)
                {
                    Drafts.Remove(key);
                    ScrollDrafts.Remove(key);
                }
            }

            public bool PromotePendingDraftDiscard(string pendingId, string sessionId)
            {
                var normalizedPending = NormalizePendingId(pendingId);
                if (DiscardedPendingDrafts.Contains(normalizedPending))
                {
                    DiscardedPendingDrafts.Remove(normalizedPending);
                    DiscardedSessionIds.Add(sessionId);
                    return true;
                }
                return false;
            }

            public void BeginPendingSend(string pendingId)
            {
                PendingSends.Add(pendingId);
            }

            public void FinishPendingSend(string pendingId)
            {
                PendingSends.Remove(pendingId);
            }

            public bool IsPendingSend(string pendingId) => PendingSends.Contains(pendingId);
            public bool IsPendingDraftDiscarded(string pendingId) => DiscardedPendingDrafts.Contains(NormalizePendingId(pendingId));
            public bool IsSessionDraftDiscarded(string sessionId) => DiscardedSessionIds.Contains(sessionId);

            public void DeleteDraftsForSession(string sessionId)
            {
                var keysToRemove = new List<string>();
                foreach (var key in Drafts.Keys)
                {
                    if (key.Contains(sessionId)) keysToRemove.Add(key);
                }
                foreach (var key in keysToRemove)
                {
                    Drafts.Remove(key);
                    ScrollDrafts.Remove(key);
                }

                // Also clear pending versions
                var pendingKeysToRemove = new List<string>();
                foreach (var pendingId in DiscardedPendingDrafts)
                {
                    if (pendingId.Contains(sessionId)) pendingKeysToRemove.Add(pendingId);
                }
                foreach (var key in pendingKeysToRemove)
                {
                    DiscardedPendingDrafts.Remove(key);
                }
            }

            public void ClearSessionDraftDiscarded(string sessionId)
            {
                DiscardedSessionIds.Remove(sessionId);
            }

            private string NormalizePendingId(string id)
            {
                // Normalize Agent Manager pending ids
                if (id.StartsWith("agent-manager:"))
                {
                    return id.Replace("agent-manager:local:pending:", "pending:");
                }
                return id;
            }

            private HashSet<string> DiscardedSessionIds { get; } = new HashSet<string>();
        }

        private class DraftData
        {
            public string Type { get; set; }
        }

        private class ReviewDraft
        {
            public string Id { get; set; }
            public string File { get; set; }
            public string Side { get; set; }
            public int Line { get; set; }
            public string Comment { get; set; }
            public string SelectedText { get; set; }
        }

        private class ImageDraft
        {
            public string Filename { get; set; }
            public string Mime { get; set; }
            public string DataUrl { get; set; }
        }

        [Fact]
        public void Stores_and_clears_all_prompt_artifacts_together()
        {
            // Arrange
            var store = new DraftStore();
            var reviews = new List<ReviewDraft> { new ReviewDraft { Id = "review", File = "a.ts", Side = "additions", Line = 1, Comment = "comment", SelectedText = "line" } };
            var images = new List<ImageDraft> { new ImageDraft { Filename = "a.png", Mime = "image/png", DataUrl = "data:image/png;base64,a" } };

            // Act
            store.SavePromptDraft("prompt:default:pending:sidebar-pending:1", "draft", reviews, images, 42);

            // Assert
            store.Drafts.Count.Should().Be(1);
            store.ReviewDrafts.Count.Should().Be(1);
            store.ImageDrafts.Count.Should().Be(1);
            store.ScrollDrafts.Count.Should().Be(1);

            // Act - discard
            store.DiscardPendingDraft("sidebar-pending:1");

            // Assert - all cleared
            store.Drafts.Count.Should().Be(0);
            store.ReviewDrafts.Count.Should().Be(0);
            store.ImageDrafts.Count.Should().Be(0);
            store.ScrollDrafts.Count.Should().Be(0);
            store.IsPendingDraftDiscarded("sidebar-pending:1").Should().BeTrue();
        }

        [Fact]
        public void Normalizes_Agent_Manager_pending_ids()
        {
            // Arrange
            var store = new DraftStore();

            // Act
            store.SavePromptDraft("agent-manager:local:pending:1", "draft", new List<ReviewDraft>(), new List<ImageDraft>(), 3);
            store.DiscardPendingDraft("pending:1");

            // Assert
            store.Drafts.Count.Should().Be(0);
            store.ScrollDrafts.Count.Should().Be(0);
        }

        [Fact]
        public void Promotes_an_in_flight_discard_marker_to_the_created_session()
        {
            // Arrange
            var store = new DraftStore();

            // Act
            store.DiscardPendingDraft("pending:promotion");
            var promoted = store.PromotePendingDraftDiscard("pending:promotion", "s1");

            // Assert
            promoted.Should().BeTrue();
            store.IsPendingDraftDiscarded("pending:promotion").Should().BeFalse();
            store.IsSessionDraftDiscarded("s1").Should().BeTrue();
        }

        [Fact]
        public void Tracks_pending_work_before_backend_submission_starts()
        {
            // Arrange
            var store = new DraftStore();

            // Act
            store.BeginPendingSend("pending:attachment");

            // Assert
            store.IsPendingSend("pending:attachment").Should().BeTrue();

            // Act - finish
            store.FinishPendingSend("pending:attachment");

            // Assert
            store.IsPendingSend("pending:attachment").Should().BeFalse();
        }

        [Fact]
        public void Deletes_session_and_pre_promotion_pending_keys()
        {
            // Arrange
            var store = new DraftStore();
            store.SavePromptDraft("prompt:default:session:s1", "session", new List<ReviewDraft>(), new List<ImageDraft>(), 1);
            store.SavePromptDraft("prompt:default:pending:s1", "pending", new List<ReviewDraft>(), new List<ImageDraft>(), 2);

            // Act
            store.DeleteDraftsForSession("s1");

            // Assert
            store.Drafts.Count.Should().Be(0);
            store.ScrollDrafts.Count.Should().Be(0);
        }
    }
}
