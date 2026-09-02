// /**
//  * Extract a human-readable error message from an unknown error value.
//  * Handles Error instances, strings, and SDK error objects (which are
//  * plain JSON objects thrown by the SDK when throwOnError is true).
//  *
//  * SDK error shapes from the server:
//  * - BadRequestError: { data: unknown, errors: [...], success: false }
//  * - NotFoundError: { name: "NotFoundError", data: { message: "..." } }
//  * - Plain string (raw text response)
//  */
// /** Extract a message from the first element of an array of strings or `{ message }` objects. */
// function firstMessage(arr: unknown): string | undefined {
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.Json;

namespace KiloVisualStudioExtension
{
    public static class ErrorHelper
    {
        static string? FirstMessage(object? arr)
        {
//   if (!Array.isArray(arr) || arr.length === 0) return undefined
            if (arr == null || !(arr is Array array) || array.Length == 0) return null;
//   const first = arr[0]
            var first = array.GetValue(0);
//   if (typeof first === "string") return first
            if (first is string str) return str;
//   if (first && typeof first === "object") {
            if (first != null && first is Dictionary<string, object?> firstObj)
            {
//     const msg = (first as Record<string, unknown>).message
                if (firstObj.TryGetValue("message", out var msgObj) && msgObj is string msg)
//     if (typeof msg === "string") return msg
                    return msg;
//   }
            }
//   return undefined
            return null;
// }
        }

// /** Extract a message from SDK error `data` field shapes (NotFoundError, ConfigInvalidError, Hono validator). */
// function messageFromData(data: Record<string, unknown>): string | undefined {
        static string? MessageFromData(Dictionary<string, object?> data)
// {
        {
//   if (typeof data.message === "string") return data.message
            if (data.TryGetValue("message", out var msgObj) && msgObj is string msg) return msg;
//   // ConfigInvalidError: { path, issues: [{ message, path, code }] }
//   const fromIssues = firstMessage(data.issues)
            if (data.TryGetValue("issues", out var issuesObj))
            {
                var fromIssues = FirstMessage(issuesObj);
//   if (fromIssues) return fromIssues
                if (fromIssues != null) return fromIssues;
            }
//   // Hono validator: { data, error: [...], success: false }
//   return firstMessage(data.error)
            if (data.TryGetValue("error", out var errorObj))
            {
                return FirstMessage(errorObj);
            }
            return null;
// }
        }

// function safeStringify(value: unknown): string | undefined {
        static string? SafeStringify(object? value)
// {
        {
//   try {
            try
//     const json = JSON.stringify(value)
            {
                var json = JsonSerializer.Serialize(value);
//     if (json !== "{}" && json.length < 500) return json
                if (json != "{}" && json.Length < 500) return json;
//   } catch (err) {
            }
            catch (Exception err)
//     console.warn("[Kilo New] getErrorMessage: JSON.stringify failed", err)
            {
                Console.WriteLine($"[Kilo New] getErrorMessage: JSON.stringify failed: {err.Message}");
//   }
            }
//   return undefined
            return null;
// }
        }

// export function getErrorMessage(error: unknown): string {
        public static string GetErrorMessage(object? error)
// {
        {
//   if (error instanceof Error) return error.message
            if (error is Exception ex) return ex.Message;
//   if (typeof error === "string") return error
            if (error is string str) return str;
//   if (!error || typeof error !== "object") return String(error)
            if (error == null || !(error is Dictionary<string, object?> obj)) return error?.ToString() ?? "";

//   const obj = error as Record<string, unknown>
//   if (typeof obj.message === "string") return obj.message
            if (obj.TryGetValue("message", out var msgObj) && msgObj is string msg) return msg;
//   if (typeof obj.error === "string") return obj.error
            if (obj.TryGetValue("error", out var errorObj) && errorObj is string errorStr) return errorStr;

//   // SDK throwOnError shape: { error: { message: "..." } }
//   if (obj.error && typeof obj.error === "object") {
            if (errorObj != null && errorObj is Dictionary<string, object?> nestedError)
            {
//     const nested = (obj.error as Record<string, unknown>).message
                if (nestedError.TryGetValue("message", out var nestedMsgObj) && nestedMsgObj is string nestedMsg)
//     if (typeof nested === "string") return nested
                    return nestedMsg;
//   }
            }

//   if (obj.data && typeof obj.data === "object") {
            if (obj.TryGetValue("data", out var dataObj) && dataObj is Dictionary<string, object?> data)
            {
//     const fromData = messageFromData(obj.data as Record<string, unknown>)
                var fromData = MessageFromData(data);
//     if (fromData) return fromData
                if (fromData != null) return fromData;
//   }
            }

//   // BadRequestError: { errors: [...] }
//   const fromErrors = firstMessage(obj.errors)
            if (obj.TryGetValue("errors", out var errorsObj))
            {
                var fromErrors = FirstMessage(errorsObj);
//   if (fromErrors) return fromErrors
                if (fromErrors != null) return fromErrors;
            }

//   return safeStringify(error) ?? String(error)
            return SafeStringify(error) ?? error.ToString() ?? "";
// }
        }
    }
}
