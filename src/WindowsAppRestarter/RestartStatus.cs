namespace WindowsAppRestarter;

internal enum RestartState
{
    Idle,
    Running,
    Succeeded,
    CompletedWithIssues,
    Failed
}

internal sealed record RestartStatus(RestartState State, string Title, string Detail, DateTimeOffset? Timestamp)
{
    public static RestartStatus Idle { get; } = new(
        RestartState.Idle,
        "Ready",
        "Restart Windows App and Explorer whenever the client stops responding.",
        null);

    public static RestartStatus Running(string step) => new(RestartState.Running, "Restarting…", step, null);

    public static RestartStatus FromResult(RestartResult result)
    {
        var state = result.Failures.Count == 0 ? RestartState.Succeeded : RestartState.CompletedWithIssues;
        var title = state == RestartState.Succeeded ? "Restart complete" : "Completed with issues";
        return new RestartStatus(state, title, result.ToSummary(), DateTimeOffset.Now);
    }

    public static RestartStatus Failure(Exception exception) =>
        new(RestartState.Failed, "Restart failed", exception.Message, DateTimeOffset.Now);

    public bool IsRunning => State == RestartState.Running;

    public string ToMenuText()
    {
        var stamp = Timestamp is { } time ? $" · {time:t}" : string.Empty;
        return $"{Title}{stamp}";
    }
}
