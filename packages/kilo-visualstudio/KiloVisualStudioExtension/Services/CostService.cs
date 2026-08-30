using System;
using System.Collections.Generic;
using System.Globalization;

namespace KiloVisualStudioExtension.Services
{
  // /**
  //  * Soft per-session max-cost nudge.
  //  *
  //  * Alert (not hard-stop) the moment a session's cumulative cost crosses a
  //  * whole-dollar threshold. The alert is non-blocking: the session keeps running
  //  * while it is shown. Continue dismisses it (won't nag again for that limit);
  //  * Stop is the surface's cue to abort.
  //  *
  //  * Cost signal: `SessionTable.cost` is written via direct SQL during message-part
  //  * projection, so `session.updated` does NOT fire on cost change. The reliable
  //  * signal is the per-assistant-message `cost`, summed here into a session total.
  //  */

  // export type MaxCostChoice = "continue" | "stop"
  public enum MaxCostChoice
  {
    Continue,
    Stop
  }

  // // Minimal shape of a message needed to aggregate session cost.
  // export interface MaxCostMessage {
  public class MaxCostMessage
  {
    //   id: string
    public string Id { get; set; } = "";
    //   sessionID: string
    public string SessionID { get; set; } = "";
    //   role?: string
    public string? Role { get; set; }
    //   cost?: number
    public double? Cost { get; set; }
  }

  // export class MaxCostNudge {
  public class CostService : ServiceProviderServiceBase
  {
    //   readonly #msgs = new Map<string, { sid: string; cost: number }>()
    private readonly Dictionary<string, MsgEntry> _msgs = new Dictionary<string, MsgEntry>();
    //   readonly #totals = new Map<string, number>()
    private readonly Dictionary<string, double> _totals = new Dictionary<string, double>();
    //   readonly #floors = new Map<string, number>()
    private readonly Dictionary<string, double> _floors = new Dictionary<string, double>();
    //   readonly #alerted = new Map<string, Set<number>>() // sid -> limit values shown this run
    private readonly Dictionary<string, HashSet<int>> _alerted = new Dictionary<string, HashSet<int>>();
    //   readonly #acked = new Map<string, Set<number>>() // sid -> limit values continued past
    private readonly Dictionary<string, HashSet<int>> _acked = new Dictionary<string, HashSet<int>>();

    //   #limit: number | undefined
    private double? _limit;
    private double _maxCost = 0;

    //   // `> 0` rounds up to whole dollars; everything else disables (undefined).
    //   static normalizeLimit(value: number | undefined | null): number | undefined {
    /// <summary>
    /// `> 0` rounds up to whole dollars; everything else disables (undefined).
    /// </summary>
    public static double? NormalizeLimit(double? value)
    {
      //     if (value == null || !Number.isFinite(value) || value <= 0) return undefined
      if (value == null || double.IsNaN((double)value) || double.IsInfinity((double)value) || (double)value <= 0) return null;
      //     return Math.ceil(value)
      return Math.Ceiling((double)value);
    }

    //   // Format a cost as `$X.XX`, with 4 decimals below $1.
    //   static formatCost(value: number): string {
    /// <summary>
    /// Format a cost as `$X.XX`, with 4 decimals below $1.
    /// </summary>
    public static string FormatCost(double value)
    {
      //     return `$${value.toFixed(value < 1 ? 4 : 2)}`
      return $"${value.ToString(value < 1 ? "F4" : "F2", CultureInfo.InvariantCulture)}";
    }

    public CostService(ServiceProvider serviceProvider) : base(serviceProvider)
    {
    }


    //   setLimit(value: number | undefined | null): void {
    /// <summary>
    /// Set the cost limit for alerts.
    /// </summary>
    public double? SetLimit(double? value)
    {
      //     this.#limit = MaxCostNudge.normalizeLimit(value)
      _limit = NormalizeLimit(value);
      return _limit;
    }

    //   get limit(): number | undefined {
    /// <summary>
    /// Get the current cost limit.
    /// </summary>
    public double? Limit
    {
      get
      {
        //     return this.#limit
        SetLimit(_maxCost);
        return _limit;
      }
    }

    // Rebuild a session's total from a full message snapshot (seed on load).
    //   resetMessageCosts(sid: string, messages: MaxCostMessage[]): number {
    /// <summary>
    /// Rebuild a session's total from a full message snapshot (seed on load).
    /// </summary>
    public double ResetMessageCosts(string sid, MaxCostMessage[] messages)
    {
      //     this.#dropMessages(sid)
      DropMessages(sid);
      //     let total = 0
      double total = 0;
      //     for (const msg of messages) {
      foreach (var msg in messages)
      {
        //       if (msg.sessionID !== sid || msg.role !== "assistant" || !Number.isFinite(msg.cost)) continue
        if (msg.SessionID != sid || msg.Role != "assistant" || double.IsNaN(msg.Cost ?? 0) || double.IsInfinity(msg.Cost ?? 0)) continue;
        //       const cost = msg.cost ?? 0
        double cost = msg.Cost ?? 0;
        //       this.#msgs.set(msg.id, { sid, cost })
        _msgs[msg.Id] = new MsgEntry { Sid = sid, Cost = cost };
        //       total += cost
        total += cost;
      }
      //     this.#totals.set(sid, total)
      _totals[sid] = total;
      //     return this.sessionCost(sid)
      return SessionCost(sid);
    }

    //   // Floor the session total with a direct cost signal (e.g. session.cost). Monotonic.
    //   setSessionCost(sid: string, value: number): number {
    /// <summary>
    /// Floor the session total with a direct cost signal (e.g. session.cost). Monotonic.
    /// </summary>
    public double SetSessionCost(string sid, double value)
    {
      //     if (Number.isFinite(value)) this.#floors.set(sid, Math.max(this.#floors.get(sid) ?? 0, value))
      if (!double.IsNaN(value) && !double.IsInfinity(value))
      {
        var existingFloor = _floors.TryGetValue(sid, out var f) ? f : 0;
        _floors[sid] = Math.Max(existingFloor, value);
      }
      //     return this.sessionCost(sid)
      return SessionCost(sid);
    }

    //   // Record an assistant message cost (message.updated). Returns the session total.
    //   updateMessageCost(sid: string, id: string, role: string | undefined, value: number | undefined): number {
    /// <summary>
    /// Record an assistant message cost (message.updated). Returns the session total.
    /// </summary>
    public double? UpdateMessageCost(string sid, string id, string? role, double? value)
    {
      //     if (role === "assistant") {
      if (role == "assistant")
      {
        //       const prev = this.#msgs.get(id)
        var prev = _msgs.TryGetValue(id, out var p) ? p : null;
        //       if (Number.isFinite(value)) {
        if (value != null && !double.IsNaN((double)value) && !double.IsInfinity((double)value))
        {
          //         if (prev && prev.sid !== sid) {
          if (prev != null && prev.Sid != sid)
          {
            //           this.#totals.set(prev.sid, Math.max(0, (this.#totals.get(prev.sid) ?? 0) - prev.cost))
            var prevTotal = _totals.TryGetValue(prev.Sid, out var t) ? t : 0;
            _totals[prev.Sid] = Math.Max(0, prevTotal - prev.Cost);
          }
          //         const before = prev?.sid === sid ? prev.cost : 0
          double before = (prev?.Sid == sid) ? prev.Cost : 0;
          //         const cost = value!
          double cost = value.Value;
          //         this.#msgs.set(id, { sid, cost })
          _msgs[id] = new MsgEntry { Sid = sid, Cost = cost };
          //         this.#totals.set(sid, Math.max(0, (this.#totals.get(sid) ?? 0) - before + cost))
          var currentTotal = _totals.TryGetValue(sid, out var ct) ? ct : 0;
          _totals[sid] = Math.Max(0, currentTotal - before + cost);
        }
        //       } else if (prev) {
        else if (prev != null)
        {
          //         // value became non-finite — drop the stale contribution
          //         this.#totals.set(prev.sid, Math.max(0, (this.#totals.get(prev.sid) ?? 0) - prev.cost))
          var prevTotal = _totals.TryGetValue(prev.Sid, out var t) ? t : 0;
          _totals[prev.Sid] = Math.Max(0, prevTotal - prev.Cost);
          //         this.#msgs.delete(id)
          _msgs.Remove(id);
        }
      }
      //     }
      //     return this.sessionCost(sid)
      return SessionCost(sid);
    }

    //   // Drop a message's contribution (message.removed).
    //   removeMessageCost(id: string): void {
    /// <summary>
    /// Drop a message's contribution (message.removed).
    /// </summary>
    public void RemoveMessageCost(string id)
    {
      //     const prev = this.#msgs.get(id)
      var prev = _msgs.TryGetValue(id, out var p) ? p : null;
      //     if (!prev) return
      if (prev == null) return;
      //     this.#msgs.delete(id)
      _msgs.Remove(id);
      //     this.#totals.set(prev.sid, Math.max(0, (this.#totals.get(prev.sid) ?? 0) - prev.cost))
      var prevTotal = _totals.TryGetValue(prev.Sid, out var t) ? t : 0;
      _totals[prev.Sid] = Math.Max(0, prevTotal - prev.Cost);
    }

    //   sessionCost(sid: string): number {
    /// <summary>
    /// Get the session cost.
    /// </summary>
    public double SessionCost(string sid)
    {
      //     return Math.max(this.#totals.get(sid) ?? 0, this.#floors.get(sid) ?? 0)
      var total = _totals.TryGetValue(sid, out var t) ? t : 0;
      var floor = _floors.TryGetValue(sid, out var f) ? f : 0;
      return Math.Max(total, floor);
    }

    //   /**
    //    * Decide whether to alert for `sid` now. Returns the limit + cost to show
    //    * once per run, or undefined (below limit, already acknowledged, or already
    //    * showing). Re-arm with {@link rearm} when the session runs again.
    //    */
    //   check(sid: string): { limit: number; cost: number } | undefined {
    /// <summary>
    /// Decide whether to alert for `sid` now. Returns the limit + cost to show
    /// once per run, or undefined (below limit, already acknowledged, or already
    /// showing). Re-arm with {@link rearm} when the session runs again.
    /// </summary>
    public AlertResult? Check(string sid)
    {
      //     const limit = this.#limit
      var limit = _limit;
      //     if (limit === undefined) return undefined
      if (limit == null) return null;
      //     const cost = this.sessionCost(sid)
      var cost = SessionCost(sid);
      //     if (cost < limit || this.#acked.get(sid)?.has(limit) || this.#alerted.get(sid)?.has(limit)) return undefined
      if (cost < limit || HasLimit(_acked, sid, (int)limit) || HasLimit(_alerted, sid, (int)limit)) return null;
      //     this.#remember(this.#alerted, sid, limit)
      Remember(_alerted, sid, (int)limit);
      //     return { limit, cost }
      return new AlertResult { Limit = (int)limit, Cost = cost };
    }

    //   // Apply the user's choice. Continue suppresses re-alerts for the current limit.
    //   resolve(sid: string, choice: MaxCostChoice, limit = this.#limit): void {
    /// <summary>
    /// Apply the user's choice. Continue suppresses re-alerts for the current limit.
    /// </summary>
    public void Resolve(string sid, MaxCostChoice choice, double? limit = null)
    {
      //     if (choice === "continue" && limit !== undefined) this.#remember(this.#acked, sid, limit)
      if (choice == MaxCostChoice.Continue && limit != null)
      {
        Remember(_acked, sid, (int)limit);
      }
    }

    //   // Re-arm alerts for a session that started running again.
    //   rearm(sid: string): void {
    /// <summary>
    /// Re-arm alerts for a session that started running again.
    /// </summary>
    public void Rearm(string sid)
    {
      //     this.#alerted.delete(sid)
      _alerted.Remove(sid);
    }

    //   // Forget all state for a deleted session.
    //   onSessionDeleted(sid: string): void {
    /// <summary>
    /// Forget all state for a deleted session.
    /// </summary>
    public void OnSessionDeleted(string sid)
    {
      //     this.#dropMessages(sid)
      DropMessages(sid);
      //     this.#totals.delete(sid)
      _totals.Remove(sid);
      //     this.#floors.delete(sid)
      _floors.Remove(sid);
      //     this.#alerted.delete(sid)
      _alerted.Remove(sid);
      //     this.#acked.delete(sid)
      _acked.Remove(sid);
    }

    //   // Drop every message contribution belonging to a session.
    //   #dropMessages(sid: string): void {
    private void DropMessages(string sid)
    {
      //     for (const [id, msg] of this.#msgs) {
      var toRemove = new List<string>();
      foreach (var kvp in _msgs)
      {
        //       if (msg.sid === sid) this.#msgs.delete(id)
        if (kvp.Value.Sid == sid) toRemove.Add(kvp.Key);
      }
      foreach (var id in toRemove)
      {
        _msgs.Remove(id);
      }
    }

    //   // Record a limit value seen for a session.
    //   #remember(map: Map<string, Set<number>>, sid: string, limit: number): void {
    private void Remember(Dictionary<string, HashSet<int>> map, string sid, int limit)
    {
      //     const seen = map.get(sid)
      if (map.TryGetValue(sid, out var seen))
      {
        //     if (seen) seen.add(limit)
        seen.Add(limit);
      }
      else
      {
        //     else map.set(sid, new Set([limit]))
        map[sid] = new HashSet<int> { limit };
      }
    }

    // Helper method to check if a session has a limit in a map
    private bool HasLimit(Dictionary<string, HashSet<int>> map, string sid, int limit)
    {
      if (map.TryGetValue(sid, out var seen))
      {
        return seen.Contains(limit);
      }
      return false;
    }

    public double MaxCostSetting()
    {
      _maxCost = SetLimit(VSExtensionSettings.Get<double>("kilo-code.new.maxCost", 0)).Value;
      return _maxCost;
    }
  }

  // Internal types
  internal class MsgEntry
  {
    public string Sid { get; set; } = "";
    public double Cost { get; set; }
  }

  public class AlertResult
  {
    public int Limit { get; set; }
    public double Cost { get; set; }
  }
}
