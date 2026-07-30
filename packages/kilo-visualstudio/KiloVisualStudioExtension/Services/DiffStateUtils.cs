using System;
using System.Collections.Generic;
using System.Linq;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Image data for diff rendering.
    /// </summary>
    public class ImageData
    {
        public string Mime { get; set; } = "";
        public int Bytes { get; set; }
        public string Data { get; set; } = "";
    }

    /// <summary>
    /// Represents a file diff in a worktree.
    /// </summary>
    public class WorktreeFileDiff
    {
        public string File { get; set; } = "";
        public string Before { get; set; } = "";
        public string After { get; set; } = "";
        public string? Patch { get; set; }
        public int Additions { get; set; }
        public int Deletions { get; set; }
        public string Status { get; set; } = "";
        public bool Tracked { get; set; }
        public bool GeneratedLike { get; set; }
        public bool Summarized { get; set; }
        public string Stamp { get; set; } = "";
        public string? Kind { get; set; }
        public ImageData? Image { get; set; }
    }

    /// <summary>
    /// Result of merging worktree diffs.
    /// </summary>
    public class MergeDiffResult
    {
        public List<WorktreeFileDiff> Diffs { get; set; } = new List<WorktreeFileDiff>();
        public HashSet<string> Stale { get; set; } = new HashSet<string>();
    }

    /// <summary>
    /// Constants for diff state management.
    /// </summary>
    public static class DiffStateConstants
    {
        public const int ExtremeDiffChangedLines = 500;
        public const int LargeContentThreshold = 8000;
    }

    /// <summary>
    /// Utility methods for diff state management.
    /// </summary>
    public static class DiffStateUtils
    {
        /// <summary>
        /// Determines if a diff should be virtualized for performance.
        /// </summary>
        public static bool ShouldVirtualizeDiff(WorktreeFileDiff diff)
        {
            return diff.Additions > DiffStateConstants.ExtremeDiffChangedLines ||
                   diff.Before.Length > DiffStateConstants.LargeContentThreshold ||
                   diff.After.Length > DiffStateConstants.LargeContentThreshold;
        }

        /// <summary>
        /// Determines if a diff is expandable (can be opened for detail view).
        /// </summary>
        public static bool IsDiffExpandable(WorktreeFileDiff diff)
        {
            return diff.Kind == "image" || 
                   (diff.Kind != "image" && diff.Kind != "audio");
        }

        /// <summary>
        /// Gets initial open files from a list of diffs.
        /// </summary>
        public static List<string> InitialOpenFiles(IEnumerable<WorktreeFileDiff> diffs)
        {
            return diffs
                .Where(d => d.Kind != "image" && d.Kind != "audio" && 
                           d.Additions <= DiffStateConstants.ExtremeDiffChangedLines)
                .Select(d => d.File)
                .ToList();
        }

        /// <summary>
        /// Gets expandable open files (includes generated and large files).
        /// </summary>
        public static List<string> ExpandableOpenFiles(IEnumerable<WorktreeFileDiff> diffs)
        {
            return diffs
                .Where(d => d.GeneratedLike || 
                           d.Additions > DiffStateConstants.ExtremeDiffChangedLines || 
                           (d.Kind != "image" && d.Kind != "audio"))
                .Select(d => d.File)
                .ToList();
        }

        /// <summary>
        /// Checks if all files are open.
        /// </summary>
        public static bool AllOpenFiles(IEnumerable<WorktreeFileDiff> diffs, IEnumerable<string> openFiles)
        {
            var openSet = new HashSet<string>(openFiles);
            return diffs.All(d => openSet.Contains(d.File));
        }

        /// <summary>
        /// Toggles open files (opens all if none open, closes all if all open).
        /// </summary>
        public static List<string> ToggleOpenFiles(IEnumerable<WorktreeFileDiff> diffs, IEnumerable<string> currentOpen)
        {
            if (AllOpenFiles(diffs, currentOpen))
                return new List<string>();
            
            return ExpandableOpenFiles(diffs);
        }

        /// <summary>
        /// Sanitizes open files by removing non-expandable files.
        /// </summary>
        public static List<string> SanitizeOpenFiles(IEnumerable<WorktreeFileDiff> diffs, IEnumerable<string> openFiles)
        {
            var openSet = new HashSet<string>(openFiles);
            return diffs
                .Where(d => IsDiffExpandable(d) && openSet.Contains(d.File))
                .Select(d => d.File)
                .ToList();
        }
    }
}
