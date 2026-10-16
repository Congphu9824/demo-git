using System.Diagnostics;
using ECommerce.Application.Common;

namespace ECommerce.Application.Diagnostics;

public static class ObservabilityActivitySource
{
    public static readonly ActivitySource Instance = new(TelemetryConstants.ActivitySourceName, TelemetryConstants.ServiceVersion);

    public static Activity? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return Instance.StartActivity(name, kind);
    }

    public static void RecordException(this Activity? activity, Exception ex)
    {
        if (activity == null) return;

        activity.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity.AddEvent(new ActivityEvent("exception", tags: new ActivityTagsCollection
        {
            { "exception.type", ex.GetType().FullName },
            { "exception.message", ex.Message },
            { "exception.stacktrace", ex.StackTrace }
        }));
    }
}
