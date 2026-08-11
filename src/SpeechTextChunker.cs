using System;
using System.Collections.Generic;
using System.Text;

namespace MateEngine.AIVoiceMod
{
    public sealed class SpeechTextChunker
    {
        private readonly StringBuilder pending = new StringBuilder();
        private FishChunkingStrategy strategy;
        private int chunkLength;

        public SpeechTextChunker(FishChunkingStrategy strategy, int chunkLength)
        {
            Configure(strategy, chunkLength);
        }

        public void Configure(FishChunkingStrategy value, int length)
        {
            strategy = value;
            chunkLength = Math.Max(100, Math.Min(300, length));
        }

        public IList<string> Push(string delta)
        {
            if (!string.IsNullOrEmpty(delta)) pending.Append(delta);
            return Drain(false);
        }

        public IList<string> Complete() { return Drain(true); }
        public void Reset() { pending.Length = 0; }

        private IList<string> Drain(bool force)
        {
            var output = new List<string>();
            while (pending.Length > 0)
            {
                var text = pending.ToString();
                int boundary = FindBoundary(text, force);
                if (boundary <= 0) break;
                var chunk = text.Substring(0, boundary).Trim();
                pending.Remove(0, boundary);
                if (chunk.Length > 0) output.Add(chunk + " ");
                if (!force) break;
            }
            return output;
        }

        private int FindBoundary(string text, bool force)
        {
            if (force) return text.Length;
            if (strategy == FishChunkingStrategy.Eager && text.Length >= 20) return Math.Min(text.Length, Math.Max(20, LastBoundary(text, 20, true)));
            if (strategy == FishChunkingStrategy.FastPhrase)
            {
                int phrase = LastBoundary(text, 20, false);
                if (phrase > 0) return phrase;
                if (text.Length >= 80) return SafeSplit(text, 80);
                return 0;
            }

            int sentence = LastSentenceBoundary(text);
            if (sentence > 0) return sentence;
            return text.Length >= chunkLength ? SafeSplit(text, chunkLength) : 0;
        }

        private static int LastSentenceBoundary(string text)
        {
            for (int i = text.Length - 1; i >= 0; i--)
                if ((text[i] == '.' || text[i] == '!' || text[i] == '?') && (i + 1 == text.Length || char.IsWhiteSpace(text[i + 1]))) return i + 1;
            return 0;
        }

        private static int LastBoundary(string text, int minimum, bool anyWhitespace)
        {
            for (int i = text.Length - 1; i >= minimum; i--)
            {
                char value = text[i];
                if (anyWhitespace ? char.IsWhiteSpace(value) : value == ',' || value == ';' || value == ':' || value == '\u2014') return i + 1;
            }
            return 0;
        }

        private static int SafeSplit(string text, int target)
        {
            int limit = Math.Min(text.Length, target);
            for (int i = limit; i >= Math.Max(20, limit - 40); i--) if (char.IsWhiteSpace(text[i - 1])) return i;
            return limit;
        }
    }
}

