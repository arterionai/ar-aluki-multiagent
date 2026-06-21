namespace Aluki.Runtime.Host.Observability;

/// <summary>
/// Centralized telemetry stage names and audit event statuses for the capture
/// lifecycle. Reused by skills, the coordinator, and telemetry instrumentation
/// to keep naming consistent across observability sinks.
/// </summary>
public static class CaptureObservability
{
    public const string ActivitySourceName = "Aluki.Capture";
    public const string MeterName = "Aluki.Capture";

    /// <summary>Critical capture pipeline stages instrumented for latency/result.</summary>
    public static class Stage
    {
        public const string Ingress = "ingress";
        public const string ScopeCheck = "scope_check";
        public const string Normalize = "normalize";
        public const string Dedupe = "dedupe";
        public const string Persist = "persist";
        public const string Audit = "audit";
        public const string RetrySchedule = "retry_schedule";
        public const string TerminalFailure = "terminal_failure";
    }

    /// <summary>Standard audit event_status values.</summary>
    public static class Status
    {
        public const string Success = "success";
        public const string Denied = "denied";
        public const string Suppressed = "suppressed";
        public const string Scheduled = "scheduled";
        public const string Failed = "failed";
    }

    /// <summary>Failure categories recorded on retry/terminal audit events.</summary>
    public static class FailureCategory
    {
        public const string Transient = "transient";
        public const string Permanent = "permanent";
        public const string ConsentStop = "consent_stop";
        public const string ScopeDenied = "scope_denied";
    }
}
