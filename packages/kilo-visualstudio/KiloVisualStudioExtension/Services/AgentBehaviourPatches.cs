namespace KiloVisualStudioExtension.Services
{
    public class AgentBehaviourPatches
    {
        public string? SelectedAgentTextOverrideValue(string text)
        {
            return string.IsNullOrEmpty(text) ? null : text;
        }

        public double? SelectedAgentNumberOverrideValue(string text, System.Func<string, double> parser)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            try
            {
                return parser(text);
            }
            catch
            {
                return null;
            }
        }

        public string? SelectedDefaultAgentValue(string value)
        {
            return string.IsNullOrEmpty(value) ? null : value;
        }

        public bool ShouldClearDefaultAgentWhenAgentBecomesUnavailable(bool isAvailable, string currentDefault, string agentName)
        {
            return !isAvailable && currentDefault == agentName;
        }
    }
}
