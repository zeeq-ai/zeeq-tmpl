using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

namespace Zeeq.Tmpl;

/// <summary>
/// Telemetry entry point with convenience methods.
/// </summary>
public static class ZeeqTelemetry
{
    public const string ActivitySourceName = "Zeeq";

    public static readonly ActivitySource Tracer = new(ActivitySourceName);

    public static readonly Meter Metrics = new(ActivitySourceName);

    public static void SetTags(params (string Key, object? Value)[] tags)
    {
        foreach (var (Key, Value) in tags)
        {
            Activity.Current?.SetTag(Key, Value);
        }
    }

    public static Activity? AddEvent(
        (string Key, object? Value)[] tags,
        string? eventName = null,
        [CallerMemberName] string name = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    )
    {
        var activity = Activity.Current;

        var effectiveName = eventName ?? $"{name}@{Path.GetFileName(filePath)}:{lineNumber}";

        activity?.AddEvent(
            new ActivityEvent(
                effectiveName,
                tags:
                [
                    .. tags.Select(tag => new KeyValuePair<string, object?>(tag.Key, tag.Value)),
                    new KeyValuePair<string, object?>("code.member", name),
                    new KeyValuePair<string, object?>("code.file_path", filePath),
                    new KeyValuePair<string, object?>("code.line_number", lineNumber),
                ]
            )
        );

        return activity;
    }

    /// <summary>
    /// Start a new trace activity with the given tags and optional trace name.
    /// Use as `using var activity = ZeeqTelemetry.Trace(tags: [("key", "value")])`
    /// The `using` is required to dispose and finalize the trace.
    /// </summary>
    public static Activity? Trace(
        (string Key, object? Value)[] tags,
        string? traceName = null,
        [CallerMemberName] string memberName = "",
        [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = 0
    ) =>
        ZeeqTelemetry.Tracer.StartActivity(
            $"{traceName ?? $"{Path.GetFileName(filePath)}#{memberName}"}",
            kind: ActivityKind.Internal,
            parentContext: Activity.Current?.Context ?? default,
            tags:
            [
                .. tags.Select(tag => new KeyValuePair<string, object?>(tag.Key, tag.Value)),
                new KeyValuePair<string, object?>("code.member", memberName),
                new KeyValuePair<string, object?>("code.file_path", filePath),
                new KeyValuePair<string, object?>("code.line_number", lineNumber),
            ]
        );
}
