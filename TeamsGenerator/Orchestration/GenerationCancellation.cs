namespace TeamsGenerator.Orchestration;

// The synchronous algorithms run inline. Cancellation is cooperative, never a detached Task race.
public static class GenerationCancellation
{
    private static readonly AsyncLocal<CancellationToken> Current = new();
    public static CancellationToken Token => Current.Value;
    public static void ThrowIfCancellationRequested() => Token.ThrowIfCancellationRequested();
    public static IDisposable Enter(CancellationToken token) => new Scope(token);

    private sealed class Scope : IDisposable
    {
        private readonly CancellationToken _previous;
        public Scope(CancellationToken token)
        {
            _previous = Current.Value;
            Current.Value = token;
        }
        public void Dispose() => Current.Value = _previous;
    }
}
