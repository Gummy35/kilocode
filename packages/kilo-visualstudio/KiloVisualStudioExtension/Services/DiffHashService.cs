using System;

namespace KiloVisualStudioExtension.Services
{
    public class DiffHashService
    {
        public string ComputeHash(string before, string after)
        {
            var combined = $"{before}---{after}";
            return combined.GetHashCode().ToString("X");
        }
    }
}
