using System.Text;

namespace VideoTriage.Core.Encoding;

/// <summary>
/// Feeds HandBrakeCLI --json stdout/stderr lines and emits a <see cref="HandBrakeProgress"/>
/// when a complete top-level JSON object has been seen. HandBrake pretty-prints objects across
/// many lines (with trailing commas), so a single line never contains a whole object.
/// </summary>
public sealed class HandBrakeProgressAccumulator
{
    private const int MaxBufferedChars = 64 * 1024; // guard against a never-closing object
    private readonly StringBuilder _buffer = new();
    private int _depth;
    private bool _capturing;

    /// <summary>Appends a line; returns parsed progress when an object completes, else null.</summary>
    public HandBrakeProgress? Append(string? line)
    {
        if (line is null) return null;

        foreach (var ch in line)
        {
            if (ch == '{')
            {
                _capturing = true;
                _depth++;
            }

            if (_capturing)
                _buffer.Append(ch);

            if (ch == '}' && _capturing)
            {
                _depth--;
                if (_depth == 0)
                {
                    var json = _buffer.ToString();
                    _buffer.Clear();
                    _capturing = false;
                    return HandBrakeProgressParser.TryParse(json);
                }
            }
        }

        if (_capturing)
        {
            _buffer.Append('\n');
            if (_buffer.Length > MaxBufferedChars)
            {
                _buffer.Clear();
                _depth = 0;
                _capturing = false;
            }
        }

        return null;
    }
}
