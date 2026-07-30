using System.Collections.Generic;

namespace KiloVisualStudioExtension.Services
{
    public class DiffImageUtils
    {
        private static readonly HashSet<string> ImageExtensions = new HashSet<string>
        {
            ".png", ".jpg", ".jpeg", ".gif", ".svg", ".webp"
        };

        public bool IsImageFile(string filename)
        {
            var ext = System.IO.Path.GetExtension(filename).ToLowerInvariant();
            return ImageExtensions.Contains(ext);
        }
    }
}
