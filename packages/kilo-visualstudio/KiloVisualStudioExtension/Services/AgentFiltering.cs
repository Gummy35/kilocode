using System;
using System.Collections.Generic;
using System.Linq;

namespace KiloVisualStudioExtension.Services
{
    /// <summary>
    /// Utility methods for agent filtering and selection.
    /// </summary>
    public static class AgentFiltering
    {
        /// <summary>
        /// Filters agents to return visible ones and determines the default agent.
        /// Excludes subagent mode and hidden agents from visible list.
        /// </summary>
        /// <param name="agents">List of agents to filter</param>
        /// <returns>Tuple of visible agents list and default agent name</returns>
        //public static (List<Agent> Visible, string DefaultAgent) FilterVisibleAgents(List<Agent> agents)
        //{
        //    var visible = agents.Where(a => a.Mode != "subagent" && !a.Hidden).ToList();
        //    var defaultAgent = visible.Count > 0 ? visible[0].Name : "code";
        //    return (visible, defaultAgent);
        //}
    }
}
