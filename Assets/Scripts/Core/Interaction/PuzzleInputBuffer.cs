using System;

namespace Session.Core.Interaction
{
    /// <summary>
    /// Accumulates the tokens a player enters into a keypad, dial bank, or lock before they are
    /// submitted to the server.
    ///
    /// Purely local — this is what the player has typed, not a claim about anything. Nothing here
    /// knows the answer, and there is no method that could compare against one. The tokens go to
    /// the server via <c>SubmitPuzzleRpc</c> and the server decides. That separation is what makes
    /// golden rule 5 hold at the UI layer too: a modified client can put whatever it likes in this
    /// buffer and still learn nothing.
    /// </summary>
    public sealed class PuzzleInputBuffer
    {
        private readonly int[] _tokens;
        private int _count;

        public PuzzleInputBuffer(int capacity)
        {
            if (capacity <= 0 || capacity > 32)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(capacity), "Puzzle input capacity must be between 1 and 32.");
            }

            _tokens = new int[capacity];
        }

        public int Capacity => _tokens.Length;

        public int Count => _count;

        public bool IsEmpty => _count == 0;

        public bool IsFull => _count == _tokens.Length;

        public ReadOnlySpan<int> Tokens => new ReadOnlySpan<int>(_tokens, 0, _count);

        /// <summary>Raised whenever the contents change, so the view can refresh without polling.</summary>
        public event Action? Changed;

        /// <summary>Append a token. Returns false when full — the view should reject the keypress audibly.</summary>
        public bool Push(int token)
        {
            if (IsFull)
            {
                return false;
            }

            _tokens[_count++] = token;
            Changed?.Invoke();
            return true;
        }

        public bool Backspace()
        {
            if (_count == 0)
            {
                return false;
            }

            _count--;
            Changed?.Invoke();
            return true;
        }

        public void Clear()
        {
            if (_count == 0)
            {
                return;
            }

            _count = 0;
            Changed?.Invoke();
        }

        /// <summary>
        /// Copy the entered tokens out for submission and clear the buffer.
        ///
        /// Clearing on commit is deliberate. A wrong four-digit code left sitting in the display
        /// means the next player to walk up starts from someone else's mistake, and in a game where
        /// two people are reading different halves of the same answer to each other, that is a
        /// genuinely maddening bug to hit.
        /// </summary>
        public int Commit(Span<int> destination)
        {
            int count = Math.Min(destination.Length, _count);

            for (int i = 0; i < count; i++)
            {
                destination[i] = _tokens[i];
            }

            Clear();
            return count;
        }

        public int TokenAt(int index)
        {
            if (index < 0 || index >= _count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _tokens[index];
        }
    }
}
