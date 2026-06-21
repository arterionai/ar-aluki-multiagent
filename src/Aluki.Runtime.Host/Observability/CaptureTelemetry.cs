using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Aluki.Runtime.Host.Observability;

/// <summary>
/// Emits critical-stage capture telemetry: per-stage latency histograms, outcome
/// counters, and distributed-tracing spans. Backed by <see cref="ActivitySource"/>
/// and <see cref="Meter"/> so any OpenTelemetry exporter can collect it.
/// </summary>
public sealed class CaptureTelemetry : IDisposable
{
    private readonly ActivitySource _activitySource;
    private readonly Meter _meter;
    private readonly Histogram<double> _stageLatencyMs;
    private readonly Counter<long> _outcomeCounter;
    private readonly Counter<long> _retryCounter;

    public CaptureTelemetry()
    {
        _activitySource = new ActivitySource(CaptureObservability.ActivitySourceName);
        _meter = new Meter(CaptureObservability.MeterName);
        _stageLatencyMs = _meter.CreateHistogram<double>(
            "aluki.capture.stage.latency",
            unit: "ms",
            description: "Latency per capture pipeline stage.");
        _outcomeCounter = _meter.CreateCounter<long>(
            "aluki.capture.outcome",
            description: "Count of capture outcomes by result.");
        _retryCounter = _meter.CreateCounter<long>(
            "aluki.capture.retry",
            description: "Count of scheduled capture retries.");
    }

    /// <summary>
    /// Times a capture stage. Dispose the returned scope to record latency,
    /// result status, and (optionally) a failure category.
    /// </summary>
    public StageScope BeginStage(string stage, string correlationId, Guid? tenantId = null)
    {
        var activity = _activitySource.StartActivity($"capture.{stage}", ActivityKind.Internal);
        activity?.SetTag("capture.stage", stage);
        activity?.SetTag("correlation_id", correlationId);
        if (tenantId is not null)
        {
            activity?.SetTag("tenant_id", tenantId);
        }

        return new StageScope(this, activity, stage);
    }

    public void RecordOutcome(string stage, string resultStatus, string? failureCategory = null)
    {
        _outcomeCounter.Add(
            1,
            new KeyValuePair<string, object?>("stage", stage),
            new KeyValuePair<string, object?>("result", resultStatus),
            new KeyValuePair<string, object?>("failure_category", failureCategory ?? "none"));
    }

    public void RecordRetry(int attemptNumber, string failureCategory)
    {
        _retryCounter.Add(
            1,
            new KeyValuePair<string, object?>("attempt", attemptNumber),
            new KeyValuePair<string, object?>("failure_category", failureCategory));
    }

    private void RecordStageLatency(string stage, double elapsedMs, string resultStatus)
    {
        _stageLatencyMs.Record(
            elapsedMs,
            new KeyValuePair<string, object?>("stage", stage),
            new KeyValuePair<string, object?>("result", resultStatus));
    }

    public void Dispose()
    {
        _activitySource.Dispose();
        _meter.Dispose();
    }

    public sealed class StageScope : IDisposable
    {
        private readonly CaptureTelemetry _owner;
        private readonly Activity? _activity;
        private readonly string _stage;
        private readonly long _startTimestamp;
        private string _resultStatus = CaptureObservability.Status.Success;
        private bool _disposed;

        internal StageScope(CaptureTelemetry owner, Activity? activity, string stage)
        {
            _owner = owner;
            _activity = activity;
            _stage = stage;
            _startTimestamp = Stopwatch.GetTimestamp();
        }

        public void SetResult(string resultStatus, string? failureCategory = null)
        {
            _resultStatus = resultStatus;
            _activity?.SetTag("result", resultStatus);
            if (failureCategory is not null)
            {
                _activity?.SetTag("failure_category", failureCategory);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            var elapsedMs = Stopwatch.GetElapsedTime(_startTimestamp).TotalMilliseconds;
            _owner.RecordStageLatency(_stage, elapsedMs, _resultStatus);
            _activity?.SetTag("latency_ms", elapsedMs);
            _activity?.Dispose();
        }
    }
}
