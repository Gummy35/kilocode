namespace KiloVisualStudioExtension.Services
{
    public class DiffSourceCatalog
    {
        public string GetDisplayName(string source)
        {
            return source.Substring(0, 1).ToUpperInvariant() + source.Substring(1);
        }
    }
}
