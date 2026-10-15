namespace ScrollSearch.Core;

/// <summary>One observation at a time; cancellation invalidates even an uncooperative OCR operation.</summary>
public sealed class ScanLoop<T>
{
    CancellationTokenSource? cancellation;
    Task completion = Task.CompletedTask;
    int generation;
    public bool Running => cancellation != null;
    public Task Completion => completion;
    public void Start(Func<CancellationToken, Task<T>> observe, Action<T> publish, Action<Exception> error, TimeSpan interval)
    {
        if (Running) return;
        cancellation = new CancellationTokenSource();
        var token = cancellation.Token; var mine = ++generation;
        completion = Run();
        async Task Run()
        {
            try
            {
                while (!token.IsCancellationRequested)
                {
                    var start = DateTimeOffset.UtcNow;
                    var result = await observe(token);
                    if (token.IsCancellationRequested || mine != generation) return;
                    publish(result);
                    var remaining = interval - (DateTimeOffset.UtcNow - start);
                    await Task.Delay(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(50), token);
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception e) { if (mine == generation && !token.IsCancellationRequested) { Stop(); error(e); } }
        }
    }
    public void Stop()
    {
        generation++;
        var old = cancellation; cancellation = null;
        old?.Cancel(); old?.Dispose();
    }
}
