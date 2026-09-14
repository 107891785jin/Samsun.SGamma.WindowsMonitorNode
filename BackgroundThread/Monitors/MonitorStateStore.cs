using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace Samsun.SGamma.WindowsMonitorNode.BackgroundThread.Monitors
{
    /// <summary>
    /// 保存规则状态、连续命中次数和日志冷却状态。警告/异常分别作为独立 RuleKey 跟踪。
    /// </summary>
    internal sealed class MonitorStateStore
    {
        private readonly ConcurrentDictionary<string, MonitorRuleState> _states = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, (DateTime capturedAt, bool valid, string error)> _resourceHealth = new(StringComparer.OrdinalIgnoreCase);

        public MonitorRuleState GetRuleState(string ruleKey)
        {
            if (string.IsNullOrWhiteSpace(ruleKey)) return null;
            _states.TryGetValue(ruleKey, out var state);
            return state;
        }

        public bool IsActive(string ruleKey)
        {
            var state = GetRuleState(ruleKey);
            if (state == null) return false;
            lock (state)
            {
                return state.Status == MonitorRuleStatus.Active;
            }
        }

        public void Remove(string ruleKey)
        {
            if (string.IsNullOrWhiteSpace(ruleKey)) return;
            _states.TryRemove(ruleKey, out _);
        }

        public void RemoveWhere(Func<MonitorRuleState, bool> predicate)
        {
            if (predicate == null) return;
            foreach (var key in _states.Keys)
            {
                if (_states.TryGetValue(key, out var state) && predicate(state))
                    _states.TryRemove(key, out _);
            }
        }

        public void MarkResourceSample(string resourceKey, DateTime capturedAt, bool valid, string error)
        {
            if (string.IsNullOrWhiteSpace(resourceKey)) return;
            _resourceHealth[resourceKey] = (capturedAt, valid, error);
        }

        public bool TryGetResourceSample(string resourceKey, out DateTime capturedAt, out bool valid, out string error)
        {
            capturedAt = DateTime.MinValue;
            valid = false;
            error = null;
            if (!_resourceHealth.TryGetValue(resourceKey, out var value)) return false;
            capturedAt = value.capturedAt;
            valid = value.valid;
            error = value.error;
            return true;
        }

        public MonitorStateChange Apply(MonitorRuleResult result, MonitorBehaviorOptions options)
        {
            var state = _states.GetOrAdd(result.RuleKey, _ => new MonitorRuleState
            {
                RuleKey = result.RuleKey,
                ResourceKey = result.ResourceKey,
                Severity = result.Severity,
                Status = MonitorRuleStatus.Unknown
            });

            lock (state)
            {
                state.LastEvaluatedAt = result.EvaluatedAt;
                state.ResourceKey = result.ResourceKey;
                state.Severity = result.Severity;

                if (!result.IsValid)
                {
                    bool changed = state.Status != MonitorRuleStatus.Unknown;
                    state.Status = MonitorRuleStatus.Unknown;
                    state.ConsecutiveHealthy = 0;
                    state.ConsecutiveViolations = 0;
                    state.LastMessage = result.Message;
                    if (changed) state.LastChangedAt = result.EvaluatedAt;

                    bool reminder = state.LastLoggedAt == DateTime.MinValue || result.EvaluatedAt - state.LastLoggedAt >= options.ActiveReminderInterval;
                    if (changed || reminder)
                    {
                        state.LastLoggedAt = result.EvaluatedAt;
                        return new MonitorStateChange
                        {
                            ShouldLog = true,
                            Severity = result.Severity,
                            RuleKey = result.RuleKey,
                            Message = result.Message
                        };
                    }
                    return new MonitorStateChange();
                }

                if (result.IsViolation)
                {
                    state.ConsecutiveHealthy = 0;
                    state.ConsecutiveViolations++;
                    int required = options.RequiredSamples(result.Severity);
                    if (state.Status != MonitorRuleStatus.Active && state.ConsecutiveViolations >= required)
                    {
                        state.Status = MonitorRuleStatus.Active;
                        state.LastChangedAt = result.EvaluatedAt;
                        state.LastLoggedAt = result.EvaluatedAt;
                        state.LastMessage = result.Message;
                        return new MonitorStateChange
                        {
                            ShouldLog = true,
                            Severity = result.Severity,
                            RuleKey = result.RuleKey,
                            Message = result.Message
                        };
                    }

                    if (state.Status == MonitorRuleStatus.Active &&
                        (state.LastLoggedAt == DateTime.MinValue || result.EvaluatedAt - state.LastLoggedAt >= options.ActiveReminderInterval))
                    {
                        state.LastLoggedAt = result.EvaluatedAt;
                        state.LastMessage = result.Message;
                        return new MonitorStateChange
                        {
                            ShouldLog = true,
                            Severity = result.Severity,
                            RuleKey = result.RuleKey,
                            Message = result.Message
                        };
                    }

                    state.LastMessage = result.Message;
                    return new MonitorStateChange();
                }

                state.ConsecutiveViolations = 0;
                state.ConsecutiveHealthy++;

                if (state.Status == MonitorRuleStatus.Active && state.ConsecutiveHealthy >= Math.Max(1, options.RecoveryConsecutiveSamples))
                {
                    state.Status = MonitorRuleStatus.Normal;
                    state.LastChangedAt = result.EvaluatedAt;
                    state.LastMessage = result.RecoveryMessage;
                    // 需求：取消恢复日志 —— 状态转正常但不写恢复提醒，仅保留节点"是否正常"状态依据。
                    return new MonitorStateChange();
                }

                if (state.Status == MonitorRuleStatus.Unknown)
                {
                    state.Status = MonitorRuleStatus.Normal;
                    state.LastChangedAt = result.EvaluatedAt;
                }

                return new MonitorStateChange();
            }
        }

        public IReadOnlyList<MonitorRuleState> SnapshotStates()
        {
            return _states.Values.Select(Clone).ToList();
        }

        private static MonitorRuleState Clone(MonitorRuleState s)
        {
            lock (s)
            {
                return new MonitorRuleState
                {
                    RuleKey = s.RuleKey,
                    ResourceKey = s.ResourceKey,
                    Severity = s.Severity,
                    Status = s.Status,
                    ConsecutiveViolations = s.ConsecutiveViolations,
                    ConsecutiveHealthy = s.ConsecutiveHealthy,
                    LastEvaluatedAt = s.LastEvaluatedAt,
                    LastChangedAt = s.LastChangedAt,
                    LastLoggedAt = s.LastLoggedAt,
                    LastMessage = s.LastMessage
                };
            }
        }
    }
}