// /** Validate and sanitize per-mode model selections from untrusted sources. */
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace KiloVisualStudioExtension
{
    public static class ModelSelectionValidator
    {
// export function validateModelSelections(raw: unknown): Record<string, { providerID: string; modelID: string }> {
        public static Dictionary<string, ModelSelection> ValidateModelSelections(object? raw)
// {
        {
//   if (!raw || typeof raw !== "object" || Array.isArray(raw)) return {}
            if (raw == null || raw is not Dictionary<string, object?>)
                return new Dictionary<string, ModelSelection>();
//   const result: Record<string, { providerID: string; modelID: string }> = {}
            var result = new Dictionary<string, ModelSelection>();
//   for (const [key, val] of Object.entries(raw as Record<string, unknown>)) {
            foreach (var kvp in (Dictionary<string, object?>)raw)
//     if (isModelSelection(val)) {
            {
                if (IsModelSelection(kvp.Value, out var selection))
//       result[key] = { providerID: val.providerID, modelID: val.modelID }
                {
                    result[kvp.Key] = selection;
                }
//     }
            }
//   }
            return result;
//   return result
        }
// }
//
// function isModelSelection(r: unknown): r is { providerID: string; modelID: string } {
        private static bool IsModelSelection(object? r, out ModelSelection selection)
// {
        {
            selection = null!;
//   return (
//     !!r &&
            if (r == null) return false;
//     typeof r === "object" &&
            if (r is not Dictionary<string, object?> dict) return false;
//     typeof (r as Record<string, unknown>).providerID === "string" &&
            if (dict.TryGetValue("providerID", out var providerIdObj) && providerIdObj is string providerId &&
//     typeof (r as Record<string, unknown>).modelID === "string"
                dict.TryGetValue("modelID", out var modelIdObj) && modelIdObj is string modelId)
//   )
            {
//   }
                selection = new ModelSelection { ProviderId = providerId, ModelId = modelId };
//     return true
                return true;
//   }
            }
// }
            return false;
        }
    }

    public class ModelSelection
    {
        public string ProviderId { get; set; } = "";
        public string ModelId { get; set; } = "";
    }
}
