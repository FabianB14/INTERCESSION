using System;

namespace Session.Core.Text
{
    /// <summary>
    /// Builds text into a caller-owned char array without allocating.
    ///
    /// This exists because golden rule 6 forbids string concatenation on a per-frame path, and a
    /// HUD is nothing but per-frame text. <c>"Room " + n</c> allocates a string every frame it
    /// changes; a timer counting down allocates sixty a second. Write into one of these instead and
    /// hand the array to TMP_Text.SetCharArray, which takes it without copying.
    ///
    /// Every Append returns false rather than throwing when the buffer is full. A truncated label
    /// is a cosmetic bug; an exception thrown from a UI update is a crash in front of the player.
    /// </summary>
    public struct TextWriteBuffer
    {
        private readonly char[] _buffer;
        private int _length;

        public TextWriteBuffer(char[] buffer)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _length = 0;
        }

        public char[] Buffer => _buffer;

        public int Length => _length;

        public int Capacity => _buffer.Length;

        public bool IsEmpty => _length == 0;

        public void Clear()
        {
            _length = 0;
        }

        public bool Append(char value)
        {
            if (_length >= _buffer.Length)
            {
                return false;
            }

            _buffer[_length++] = value;
            return true;
        }

        public bool Append(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return true;
            }

            if (_length + value!.Length > _buffer.Length)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                _buffer[_length++] = value[i];
            }

            return true;
        }

        /// <summary>Append an integer without going through <c>ToString</c>, which would allocate.</summary>
        public bool Append(int value)
        {
            if (value == 0)
            {
                return Append('0');
            }

            bool negative = value < 0;

            // int.MinValue has no positive counterpart, so accumulate on the negative side.
            Span<char> digits = stackalloc char[11];
            int count = 0;

            if (negative)
            {
                while (value != 0)
                {
                    int digit = -(value % 10);
                    digits[count++] = (char)('0' + digit);
                    value /= 10;
                }
            }
            else
            {
                while (value != 0)
                {
                    digits[count++] = (char)('0' + (value % 10));
                    value /= 10;
                }
            }

            int needed = count + (negative ? 1 : 0);
            if (_length + needed > _buffer.Length)
            {
                return false;
            }

            if (negative)
            {
                _buffer[_length++] = '-';
            }

            for (int i = count - 1; i >= 0; i--)
            {
                _buffer[_length++] = digits[i];
            }

            return true;
        }

        /// <summary>Append zero-padded to at least <paramref name="digits"/> characters.</summary>
        public bool AppendPadded(int value, int digits)
        {
            if (value < 0 || digits < 1)
            {
                return Append(value);
            }

            int magnitude = 10;
            int used = 1;
            while (value >= magnitude && used < 10)
            {
                magnitude *= 10;
                used++;
            }

            for (int i = used; i < digits; i++)
            {
                if (!Append('0'))
                {
                    return false;
                }
            }

            return Append(value);
        }

        /// <summary>
        /// Append a duration as M:SS, or H:MM:SS past an hour. Negative durations clamp to 0:00 —
        /// a room's remaining allowance goes negative the moment it is overrun, and "-1:-3" on
        /// screen would be worse than useless.
        /// </summary>
        public bool AppendDuration(float totalSeconds)
        {
            if (totalSeconds < 0f || float.IsNaN(totalSeconds))
            {
                totalSeconds = 0f;
            }

            int whole = (int)totalSeconds;
            int hours = whole / 3600;
            int minutes = (whole % 3600) / 60;
            int seconds = whole % 60;

            if (hours > 0)
            {
                return Append(hours) && Append(':') && AppendPadded(minutes, 2) && Append(':')
                       && AppendPadded(seconds, 2);
            }

            return Append(minutes) && Append(':') && AppendPadded(seconds, 2);
        }

        /// <summary>
        /// True when the buffer's contents differ from <paramref name="other"/>. UI code uses this
        /// to skip pushing identical text into TMP, which is the expensive part.
        /// </summary>
        public bool DiffersFrom(char[] other, int otherLength)
        {
            if (other == null || otherLength != _length)
            {
                return true;
            }

            for (int i = 0; i < _length; i++)
            {
                if (_buffer[i] != other[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>Copy out. Allocates — for tests and logging only, never a per-frame path.</summary>
        public override string ToString() => new string(_buffer, 0, _length);
    }
}
