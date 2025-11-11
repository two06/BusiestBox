using System;
using System.Collections.Generic;

namespace BusiestBox.UI
{
    // Minimal context passed to providers
    public sealed class CompletionContext
    {
        public string CurrentDirectory { get; set; }
        public string FullLine { get; set; }      // entire buffer (no prompt)
        public int CursorPos { get; set; }        // caret within FullLine
        public string[] Commands { get; set; }    // top-level commands (first token)
        public int TokenStart { get; set; }       // index of token under caret
        public string CurrentToken { get; set; }  // token text under caret
        public string[] ArgsUpToCursor { get; set; } // args parsed from start..caret (quote-aware)
    }

    public interface ICompletionProvider
    {
        // Return true if you handled the context (even if you had 0 matches).
        bool TryGetCompletions(CompletionContext ctx, out string[] matches);
    }

    public static class CompletionRegistry
    {
        private static readonly List<ICompletionProvider> _providers = new List<ICompletionProvider>();

        public static void Register(ICompletionProvider provider)
        {
            if (provider != null) _providers.Add(provider);
        }

        public static bool TryGetCompletions(CompletionContext ctx, out string[] matches)
        {
            for (int i = 0; i < _providers.Count; i++)
            {
                if (_providers[i].TryGetCompletions(ctx, out matches))
                    return true;
            }
            matches = null;
            return false;
        }
    }
}
