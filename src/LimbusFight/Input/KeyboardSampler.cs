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

    /// <summary>Player 2: arrows to move / jump; skills on numpad 1-3 or , . /; defend on numpad 0 or Right Shift; E.G.O on numpad Enter or '.</summary>
    public static readonly IReadOnlyList<(KeyboardKey Key, InputButtons Button)> Player2Bindings = new[]
    {
        (KeyboardKey.Left, InputButtons.Left),
        (KeyboardKey.Right, InputButtons.Right),
        (KeyboardKey.Up, InputButtons.Up),
        (KeyboardKey.Kp1, InputButtons.Punch),
        (KeyboardKey.Comma, InputButtons.Punch),
        (KeyboardKey.Kp2, InputButtons.Skill2),
        (KeyboardKey.Period, InputButtons.Skill2),
        (KeyboardKey.Kp3, InputButtons.Skill3),
        (KeyboardKey.Slash, InputButtons.Skill3),
        (KeyboardKey.Kp0, InputButtons.Defend),
        (KeyboardKey.RightShift, InputButtons.Defend),
        (KeyboardKey.KpEnter, InputButtons.Ego),
        (KeyboardKey.Apostrophe, InputButtons.Ego),
    };

    /// <summary>Gamepad buttons for fighting (Xbox names; PlayStation: Cross, Square, Triangle, Circle).</summary>
    public static readonly IReadOnlyList<(GamepadButton Button, InputButtons Input)> PadBindings = new[]
    {
        (GamepadButton.LeftFaceLeft, InputButtons.Left),
        (GamepadButton.LeftFaceRight, InputButtons.Right),
        (GamepadButton.LeftFaceUp, InputButtons.Up),
        (GamepadButton.RightFaceDown, InputButtons.Punch),
        (GamepadButton.RightFaceLeft, InputButtons.Skill2),
        (GamepadButton.RightFaceUp, InputButtons.Skill3),
        (GamepadButton.RightFaceRight, InputButtons.Defend),
        (GamepadButton.LeftTrigger1, InputButtons.Defend),
        (GamepadButton.RightTrigger1, InputButtons.Ego),
        (GamepadButton.RightTrigger2, InputButtons.Ego),
    };

    private const float StickDeadZone = 0.5f;
    private const int MaxPads = 2;

    private readonly InputButtons[] _padHeld = new InputButtons[MaxPads];
    private readonly InputButtons[] _padPrevious = new InputButtons[MaxPads];
    private readonly HashSet<(int Pad, GamepadButton Button)> _padPressedThisFrame = new();
    private readonly HashSet<KeyboardKey> _pressedThisFrame = new();
    private InputButtons _pressedSinceLastTick;
    private InputButtons _p2PressedSinceLastTick;

    /// <summary>Which player the first controller plays as (1 or 2); the second controller plays as the other.</summary>
    public int Pad1Player { get; set; } = 1;

    /// <summary>One-player modes: every controller plays as Player 1.</summary>
    public bool SinglePlayer { get; set; } = true;

    /// <summary>How many controllers Raylib sees (for the options screen).</summary>
    public int PadCount { get; private set; }

    /// <summary>Call exactly once per render frame, before hotkeys and simulation ticks.</summary>
    public void Poll()
    {
        _pressedThisFrame.Clear();
        PollPads();

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

        foreach ((KeyboardKey bound, InputButtons button) in Player2Bindings)
        {
            if (WasPressed(bound))
            {
                _p2PressedSinceLastTick |= button;
            }
        }
    }

    /// <summary>Reads both controllers: held fight buttons (d-pad or left stick), newly pressed buttons latched per player.</summary>
    private void PollPads()
    {
        _padPressedThisFrame.Clear();
        PadCount = 0;
        for (int pad = 0; pad < MaxPads; pad++)
        {
            _padPrevious[pad] = _padHeld[pad];
            _padHeld[pad] = InputButtons.None;
            if (!Raylib.IsGamepadAvailable(pad))
            {
                continue;
            }

            PadCount++;
            InputButtons held = InputButtons.None;
            foreach ((GamepadButton button, InputButtons input) in PadBindings)
            {
                if (Raylib.IsGamepadButtonDown(pad, button))
                {
                    held |= input;
                }
            }

            float x = Raylib.GetGamepadAxisMovement(pad, GamepadAxis.LeftX);
            float y = Raylib.GetGamepadAxisMovement(pad, GamepadAxis.LeftY);
            held |= x < -StickDeadZone ? InputButtons.Left : x > StickDeadZone ? InputButtons.Right : InputButtons.None;
            held |= y < -0.6f ? InputButtons.Up : InputButtons.None;
            _padHeld[pad] = held;

            foreach (GamepadButton b in Enum.GetValues<GamepadButton>())
            {
                if (b != GamepadButton.Unknown && Raylib.IsGamepadButtonPressed(pad, b))
                {
                    _padPressedThisFrame.Add((pad, b));
                }
            }

            // The stick pushed past the dead zone counts as a d-pad press for menus.
            if ((held & InputButtons.Left) != 0 && (_padPrevious[pad] & InputButtons.Left) == 0)
            {
                _padPressedThisFrame.Add((pad, GamepadButton.LeftFaceLeft));
            }

            if ((held & InputButtons.Right) != 0 && (_padPrevious[pad] & InputButtons.Right) == 0)
            {
                _padPressedThisFrame.Add((pad, GamepadButton.LeftFaceRight));
            }

            bool stickUp = y < -0.6f, stickDown = y > 0.6f;
            if (stickUp && !_stickWasUp[pad])
            {
                _padPressedThisFrame.Add((pad, GamepadButton.LeftFaceUp));
            }

            if (stickDown && !_stickWasDown[pad])
            {
                _padPressedThisFrame.Add((pad, GamepadButton.LeftFaceDown));
            }

            _stickWasUp[pad] = stickUp;
            _stickWasDown[pad] = stickDown;

            InputButtons newly = held & ~_padPrevious[pad];
            if (PlayerOfPad(pad) == 1)
            {
                _pressedSinceLastTick |= newly;
            }
            else
            {
                _p2PressedSinceLastTick |= newly;
            }
        }
    }

    private readonly bool[] _stickWasUp = new bool[MaxPads];
    private readonly bool[] _stickWasDown = new bool[MaxPads];

    /// <summary>Which player (1 or 2) a controller plays as right now.</summary>
    public int PlayerOfPad(int pad) => SinglePlayer ? 1 : (pad == 0 ? Pad1Player : 3 - Pad1Player);

    /// <summary>
    /// True if a controller button went down this frame. <paramref name="player"/> 0 = any controller,
    /// 1 / 2 = only the controller playing as that player.
    /// </summary>
    public bool PadPressed(GamepadButton button, int player = 0)
    {
        for (int pad = 0; pad < MaxPads; pad++)
        {
            if ((player == 0 || PlayerOfPad(pad) == player) && _padPressedThisFrame.Contains((pad, button)))
            {
                return true;
            }
        }

        return false;
    }

    private InputButtons PadHeldFor(int player)
    {
        InputButtons held = InputButtons.None;
        for (int pad = 0; pad < MaxPads; pad++)
        {
            if (PlayerOfPad(pad) == player)
            {
                held |= _padHeld[pad];
            }
        }

        return held;
    }

    /// <summary>Player 2's input for one simulation tick (latched presses + held keys).</summary>
    public FrameInput ConsumeTickPlayer2()
    {
        InputButtons held = _p2PressedSinceLastTick | PadHeldFor(2);
        foreach ((KeyboardKey key, InputButtons button) in Player2Bindings)
        {
            if (Raylib.IsKeyDown(key))
            {
                held |= button;
            }
        }

        _p2PressedSinceLastTick = InputButtons.None;
        return new FrameInput(held);
    }

    /// <summary>True if the key went down during this render frame (including sub-frame taps).</summary>
    public bool WasPressed(KeyboardKey key) => _pressedThisFrame.Contains(key) || Raylib.IsKeyPressed(key);

    /// <summary>Call once per simulation tick; returns that tick's input and clears the latch.</summary>
    public FrameInput ConsumeTick()
    {
        InputButtons held = _pressedSinceLastTick | PadHeldFor(1);

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
    public void Clear()
    {
        _pressedSinceLastTick = InputButtons.None;
        _p2PressedSinceLastTick = InputButtons.None;
    }
}
