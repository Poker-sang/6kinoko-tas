using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace KinokoTAS.Core;

// Future transport adapter; deliberately no fake implementation or timing-based stepping.
public sealed record GameState(long Frame, bool Paused, string BuildIdentity);

public interface IGameSession : IAsyncDisposable
{
    Task<GameState> PauseAsync(CancellationToken cancellationToken);

    Task<GameState> StepAsync(IReadOnlyList<bool> actions, CancellationToken cancellationToken);

    Task<GameState> ResumeAsync(double speed, CancellationToken cancellationToken);
}
