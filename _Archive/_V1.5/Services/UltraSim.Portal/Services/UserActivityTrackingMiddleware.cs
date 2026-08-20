namespace UltraSim.Portal.Services;

public sealed class UserActivityTrackingMiddleware : IMiddleware
{
    private readonly UserActivityTracker _userActivityTracker;

    public UserActivityTrackingMiddleware(UserActivityTracker userActivityTracker)
    {
        _userActivityTracker = userActivityTracker;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        await _userActivityTracker.TrackAsync(context.User, context.RequestAborted);
        await next(context);
    }
}
