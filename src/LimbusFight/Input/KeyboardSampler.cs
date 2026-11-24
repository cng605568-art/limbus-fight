using LimbusFight.Gameplay;
using Raylib_cs;

namespace LimbusFight.Input;

/// <summary>
/// Turns Raylib keyboard state into one <see cref="FrameInput"/> per simulation tick, and
/// answers "was this key pressed this frame?" for hotkeys.
///
/// Raylib's IsKeyPressed misses a tap that goes down AND up inside one frame, but its key
/// queue (GetKeyPressed) does not. This class drains that queue every frame and latches the
/// presses until the next simulation tick, so a quick jab is never eaten, even on a frame
/// that runs zero simulation ticks.
/// </summary>
public sealed class KeyboardSampler
{
    /// <summary>Player 1 key bindings for Step 1.</summary>
    public static readonly IReadOnlyList<(KeyboardKey Key, InputButtons Button)> Player1Bindings = new[]
    {
        (KeyboardKey.A, InputButtons.Left),
        (KeyboardKey.D, InputButtons.Right),
        (KeyboardKey.W, InputButtons.Up),
        (KeyboardKey.J, InputButtons.Punch),
        (KeyboardKey.K, InputButtons.Skill2),
        (KeyboardKey.L, InputButtons.Skill3),
        (KeyboardKey.S, InputButtons.Defend),
        (KeyboardKey.I, InputButtons.Ego),
    };

    private readonly HashSet<KeyboardKey> _pressedThisFrame = new();
    private InputButtons _pressedSinceLastTick;

    /// <summary>Call exactly once per render frame, before hotkeys and simulation ticks.</summary>
    public void Poll()
    {
        _pressedThisFrame.Clear();

        int key;
        while ((key = Raylib.GetKeyPressed()) != 0)
        {
            _pressedThisFrame.Add((KeyboardKey)key);
        }

        foreach ((KeyboardKey bound, InputButtons button) in Player1Bindings)
        {
            if (WasPressed(bound))
            {
                _pressedSinceLastTick |= button;
            }
        }
    }

    /// <summary>True if the key went down during this render frame (including sub-frame taps).</summary>
    public bool WasPressed(KeyboardKey key) => _pressedThisFrame.Contains(key) || Raylib.IsKeyPressed(key);

    /// <summary>Call once per simulation tick; returns that tick's input and clears the latch.</summary>
    public FrameInput ConsumeTick()
    {
        InputButtons held = _pressedSinceLastTick;

        foreach ((KeyboardKey key, InputButtons button) in Player1Bindings)
        {
            if (Raylib.IsKeyDown(key))
            {
                held |= button;
            }
        }

        _pressedSinceLastTick = InputButtons.None;
        return new FrameInput(held);
    }

    /// <summary>Drops latched presses (used while gameplay is paused by the warning screen).</summary>
    public void Clear() => _pressedSinceLastTick = InputButtons.None;
}
