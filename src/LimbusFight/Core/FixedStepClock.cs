namespace LimbusFight.Core;

/// <summary>
/// Converts variable render-frame time into whole 60 Hz simulation ticks.
///
/// Fighting games must simulate in exact, discrete frames (frame data, hitstun, inputs all count
/// frames). Raylib's SetTargetFPS(60) paces the render loop, and this clock guarantees the game
/// logic advances exactly 60 ticks per real second even if the OS / driver forces a different
/// refresh rate. Frame times within a small tolerance of a whole tick are snapped, so ordinary
/// timer jitter never causes a doubled or skipped logic frame.
/// </summary>
public sealed class FixedStepClock
{
    /// <summary>Upper bound on catch-up ticks per render frame (prevents a "spiral of death" after a stall).</summary>
    public const int MaxTicksPerFrame = 4;

    /// <summary>Frame times this close (seconds) to a whole number of ticks are snapped to it.</summary>
    private const double SnapTolerance = 0.0012;

    private double _accumulator;

    public FixedStepClock(int ticksPerSecond)
    {
        if (ticksPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ticksPerSecond));
        }

        TicksPerSecond = ticksPerSecond;
        StepSeconds = 1.0 / ticksPerSecond;
    }

    public int TicksPerSecond { get; }

    public double StepSeconds { get; }

    /// <summary>Total ticks produced since construction.</summary>
    public long TotalTicks { get; private set; }

    /// <summary>Ticks produced by the most recent <see cref="Advance"/> call (normally 1).</summary>
    public int LastTickCount { get; private set; }

    /// <summary>Feed the elapsed render-frame time; returns how many simulation ticks to run now.</summary>
    public int Advance(double frameSeconds)
    {
        if (double.IsNaN(frameSeconds) || frameSeconds < 0)
        {
            frameSeconds = 0;
        }

        // Snap near-integer multiples of the step (e.g. 0.01671 s -> exactly 1/60 s).
        for (int n = 1; n <= MaxTicksPerFrame; n++)
        {
            double target = n * StepSeconds;
            if (Math.Abs(frameSeconds - target) < SnapTolerance)
            {
                frameSeconds = target;
                break;
            }
        }

        _accumulator += Math.Min(frameSeconds, MaxTicksPerFrame * StepSeconds);

        int ticks = 0;
        while (_accumulator >= StepSeconds - 1e-9 && ticks < MaxTicksPerFrame)
        {
            _accumulator -= StepSeconds;
            ticks++;
        }

        if (ticks == MaxTicksPerFrame)
        {
            // We were badly behind (window drag, breakpoint, etc.). Drop the backlog instead of
            // fast-forwarding the match.
            _accumulator = 0;
        }

        _accumulator = Math.Max(_accumulator, 0);
        TotalTicks += ticks;
        LastTickCount = ticks;
        return ticks;
    }
}
