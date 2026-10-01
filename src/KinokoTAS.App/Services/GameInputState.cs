using System.Collections.Generic;
using Avalonia.Input;

namespace KinokoTAS.App.Services;

internal sealed class GameInputState
{
    private readonly HashSet<Key> _keys = [];
    private static readonly (Key Key, uint Mask)[] _Bindings =
    [
        (Key.Left, (1u << 0) | (1u << 15)),
        (Key.Right, (1u << 1) | (1u << 16)),
        (Key.Up, (1u << 2) | (1u << 8) | (1u << 9) | (1u << 17)),
        (Key.Down, (1u << 3) | (1u << 10) | (1u << 18)),
        (Key.Z, (1u << 4) | (1u << 11)),
        (Key.X, (1u << 5) | (1u << 6) | (1u << 7)),
        (Key.A, (1u << 12) | (1u << 13)),
        (Key.C, 1u << 14),
        (Key.Space, 1u << 4),
        (Key.Enter, 1u << 11),
        (Key.Escape, 1u << 13)
    ];

    public void Press(Key key) => _keys.Add(key);

    public void Release(Key key) => _keys.Remove(key);

    public void Clear() => _keys.Clear();

    public uint ReadMask(bool focused)
    {
        if (!focused)
            return 0;

        uint mask = 0;
        foreach (var binding in _Bindings)
            if (_keys.Contains(binding.Key))
                mask |= binding.Mask;

        return mask;
    }
}
