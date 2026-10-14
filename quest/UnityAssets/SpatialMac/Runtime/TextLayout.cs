using System;
using System.Collections.Generic;
using System.Text;

namespace SpatialMac {
    // Plain-string layout helpers for TextMesh (which cannot wrap). Engine-free so they can be unit-tested.
    public static class TextLayout {
        public static string Truncate(string s, int max) {
            if (string.IsNullOrEmpty(s) || max < 1) return "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }
        // Greedy word wrap. Honors explicit newlines, hard-breaks words longer than a line, and caps the result at
        // maxLines (the last line ends with an ellipsis if text was cut).
        public static string Wrap(string text, int columns, int maxLines) {
            if (string.IsNullOrEmpty(text) || columns < 1 || maxLines < 1) return "";
            var lines = new List<string>();
            foreach (string paragraph in text.Replace("\r", "").Split('\n')) {
                var cur = new StringBuilder();
                foreach (string raw in paragraph.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)) {
                    string word = raw;
                    while (word.Length > columns) {
                        if (cur.Length > 0) { lines.Add(cur.ToString()); cur.Clear(); }
                        lines.Add(word.Substring(0, columns));
                        word = word.Substring(columns);
                    }
                    if (cur.Length == 0) cur.Append(word);
                    else if (cur.Length + 1 + word.Length <= columns) cur.Append(' ').Append(word);
                    else { lines.Add(cur.ToString()); cur.Clear(); cur.Append(word); }
                }
                lines.Add(cur.ToString());
            }
            while (lines.Count > 0 && lines[lines.Count - 1].Length == 0) lines.RemoveAt(lines.Count - 1);
            if (lines.Count > maxLines) {
                lines = lines.GetRange(0, maxLines);
                string last = lines[maxLines - 1];
                lines[maxLines - 1] = last.Length >= columns ? last.Substring(0, Math.Max(0, columns - 1)) + "…" : last + "…";
            }
            return string.Join("\n", lines.ToArray());
        }
    }
}
