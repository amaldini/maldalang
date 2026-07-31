// Copyright (c) 2026 Andrea Maldini
// SPDX-License-Identifier: MIT OR Apache-2.0

namespace MaldaLang.Interpreter;

/// <summary>
/// Coerces values at integer sinks (counts, indexes, seeds, …).
/// Whole-valued floats such as <c>math.floor(n)</c> are accepted; fractional floats are not.
/// </summary>
public static class NumericCoercion
{
    public static bool TryAsInteger(RuntimeValue value, out int result)
    {
        switch (value.Type)
        {
            case ValueType.Integer:
                result = value.AsInteger();
                return true;

            case ValueType.Float:
            {
                var d = value.AsFloat();
                if (double.IsFinite(d))
                {
                    var truncated = Math.Truncate(d);
                    if (d == truncated && d >= int.MinValue && d <= int.MaxValue)
                    {
                        result = (int)truncated;
                        return true;
                    }
                }

                break;
            }
        }

        result = 0;
        return false;
    }

    public static bool IsIntegerLike(RuntimeValue value) => TryAsInteger(value, out _);
}
