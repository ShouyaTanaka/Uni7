using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Uni7.Diagnostics
{
    /// <summary>Long text / JSON hybrid viewer used by UTConsoleWindow.</summary>
    internal sealed class UTLogContentViewer
    {
        private enum ViewMode { Formatted, Raw, Tree }
        private ViewMode mode;
        private Vector2 textScroll;
        private Vector2 treeScroll;
        private string search = string.Empty;
        private bool wrap = true;
        private bool showLineNumbers = true;
        private bool expandAll = true;
        private string lastSource;
        private string formatted;
        private JsonNode tree;
        private string parseError;
        private readonly HashSet<string> collapsed = new HashSet<string>();
        private readonly HashSet<string> expanded = new HashSet<string>();
        private GUIStyle codeStyle;
        private GUIStyle lineStyle;

        public void Draw(string source, string title)
        {
            source = source ?? string.Empty;
            if (!string.Equals(lastSource, source, StringComparison.Ordinal))
            {
                lastSource = source;
                formatted = null;
                tree = null;
                parseError = null;
                collapsed.Clear();
                expanded.Clear();
            }

            EditorGUILayout.BeginVertical();
            DrawToolbar(source, title);
            DrawSearchBar();

            string displayed = mode == ViewMode.Raw ? source : GetFormatted(source);
            int lines = CountLines(displayed);
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(source.Length.ToString("N0") + " chars", EditorStyles.miniLabel, GUILayout.Width(85));
            GUILayout.Label(CountLines(source).ToString("N0") + " source lines", EditorStyles.miniLabel, GUILayout.Width(105));
            GUILayout.Label(lines.ToString("N0") + " displayed lines", EditorStyles.miniLabel, GUILayout.Width(125));
            GUILayout.FlexibleSpace();
            wrap = GUILayout.Toggle(wrap, "Wrap", EditorStyles.toolbarButton);
            showLineNumbers = GUILayout.Toggle(showLineNumbers, "Line No.", EditorStyles.toolbarButton);
            if (GUILayout.Button("Copy All", EditorStyles.toolbarButton))
                EditorGUIUtility.systemCopyBuffer = source;
            EditorGUILayout.EndHorizontal();

            if (mode == ViewMode.Tree)
                DrawTree(source);
            else
                DrawText(displayed);

            EditorGUILayout.EndVertical();
        }

        private void DrawToolbar(string source, string title)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label(title, EditorStyles.boldLabel, GUILayout.Width(75));
            DrawModeButton("Formatted", ViewMode.Formatted);
            DrawModeButton("Raw", ViewMode.Raw);
            DrawModeButton("Tree", ViewMode.Tree);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Copy Display", EditorStyles.toolbarButton))
                EditorGUIUtility.systemCopyBuffer = mode == ViewMode.Tree ? source : (mode == ViewMode.Raw ? source : GetFormatted(source));
            if (GUILayout.Button("Pop Out", EditorStyles.toolbarButton))
                UTLogContentViewerWindow.Open(title, source);
            EditorGUILayout.EndHorizontal();
        }

        private void DrawModeButton(string label, ViewMode target)
        {
            bool active = mode == target;
            if (GUILayout.Toggle(active, label, EditorStyles.toolbarButton) != active)
                mode = target;
        }

        private void DrawSearchBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Find", EditorStyles.miniLabel, GUILayout.Width(32));
            search = GUILayout.TextField(search, EditorStyles.toolbarTextField);
            if (GUILayout.Button("×", EditorStyles.toolbarButton, GUILayout.Width(22))) search = string.Empty;
            EditorGUILayout.EndHorizontal();
        }

        private void DrawText(string content)
        {
            string[] lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            float lineHeight = EditorGUIUtility.singleLineHeight + 3f;
            float width = Mathf.Max(100, EditorGUIUtility.currentViewWidth - 35);
            float contentWidth = wrap ? width : Mathf.Max(width, EstimateWidth(lines));
            textScroll = EditorGUILayout.BeginScrollView(textScroll, !wrap, true, GUILayout.ExpandHeight(true));
            EditorGUILayout.BeginHorizontal();
            if (showLineNumbers)
            {
                EditorGUILayout.BeginVertical(GUILayout.Width(42));
                for (int i = 0; i < lines.Length; i++)
                    GUILayout.Label((i + 1).ToString(), GetLineStyle(), GUILayout.Height(lineHeight));
                EditorGUILayout.EndVertical();
            }
            EditorGUILayout.BeginVertical(GUILayout.MinWidth(contentWidth));
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                string highlighted = mode == ViewMode.Formatted ? HighlightJson(line) : Escape(line);
                if (!string.IsNullOrEmpty(search) && line.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0)
                    highlighted = "<color=#FFE082>" + highlighted + "</color>";
                GUILayout.Label(highlighted, GetCodeStyle(), GUILayout.MinHeight(lineHeight), wrap ? GUILayout.ExpandWidth(true) : GUILayout.Width(contentWidth));
            }
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndScrollView();
        }

        private void DrawTree(string source)
        {
            EnsureTree(source);
            if (tree == null)
            {
                EditorGUILayout.HelpBox("Tree表示できません: " + (parseError ?? "Invalid JSON"), MessageType.Warning);
                if (GUILayout.Button("Formatted表示に切り替え")) mode = ViewMode.Formatted;
                return;
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (GUILayout.Button("Expand All", EditorStyles.toolbarButton)) { expandAll = true; collapsed.Clear(); expanded.Clear(); }
            if (GUILayout.Button("Collapse All", EditorStyles.toolbarButton)) { expandAll = false; collapsed.Clear(); expanded.Clear(); }
            EditorGUILayout.EndHorizontal();
            treeScroll = EditorGUILayout.BeginScrollView(treeScroll, GUILayout.ExpandHeight(true));
            DrawNode(tree, "root", "$", 0);
            EditorGUILayout.EndScrollView();
        }

        private void DrawNode(JsonNode node, string key, string path, int depth)
        {
            if (node == null) return;
            if (!string.IsNullOrEmpty(search) && !ContainsSearch(node, key, path)) return;
            bool container = node.Kind == JsonNodeKind.Object || node.Kind == JsonNodeKind.Array;
            string label = key + (node.Kind == JsonNodeKind.Object ? " {" : node.Kind == JsonNodeKind.Array ? " [" : ": " + node.Value);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(depth * 16);
            if (container)
            {
                bool isCollapsed = expandAll ? collapsed.Contains(path) : !expanded.Contains(path);
                bool next = EditorGUILayout.Foldout(!isCollapsed, label + "  (" + node.Children.Count + ")", true);
                if (next != !isCollapsed)
                {
                    if (expandAll) { if (next) collapsed.Remove(path); else collapsed.Add(path); }
                    else { if (next) expanded.Add(path); else expanded.Remove(path); }
                }
            }
            else
            {
                Color old = GUI.contentColor;
                GUI.contentColor = node.Kind == JsonNodeKind.String ? new Color(0.55f, 0.82f, 1f) :
                    node.Kind == JsonNodeKind.Number ? new Color(0.65f, 0.9f, 0.65f) :
                    node.Kind == JsonNodeKind.Boolean || node.Kind == JsonNodeKind.Null ? new Color(0.95f, 0.72f, 0.42f) : old;
                GUILayout.Label(label, EditorStyles.label);
                GUI.contentColor = old;
            }
            EditorGUILayout.EndHorizontal();

            if (container && (expandAll ? !collapsed.Contains(path) : expanded.Contains(path)))
            {
                foreach (KeyValuePair<string, JsonNode> child in node.Children)
                    DrawNode(child.Value, child.Key, path + "." + child.Key, depth + 1);
            }
        }

        private bool ContainsSearch(JsonNode node, string key, string path)
        {
            if (key.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0 ||
                (node.Value != null && node.Value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0))
                return true;
            foreach (KeyValuePair<string, JsonNode> child in node.Children)
                if (ContainsSearch(child.Value, child.Key, path + "." + child.Key)) return true;
            return false;
        }

        private void EnsureTree(string source)
        {
            if (tree != null || parseError != null) return;
            try { tree = new JsonParser(source).Parse(); }
            catch (Exception ex) { parseError = ex.Message; }
        }

        private string GetFormatted(string source)
        {
            if (formatted != null) return formatted;
            try
            {
                // Validate first; non-JSON text remains byte-for-byte unchanged.
                new JsonParser(source).Parse();
                formatted = JsonFormatter.Format(source);
            }
            catch
            {
                formatted = source;
            }
            return formatted;
        }

        private GUIStyle GetCodeStyle()
        {
            if (codeStyle == null)
            {
                codeStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = wrap, font = EditorStyles.label.font };
                codeStyle.padding = new RectOffset(4, 4, 1, 1);
            }
            codeStyle.wordWrap = wrap;
            return codeStyle;
        }

        private GUIStyle GetLineStyle()
        {
            if (lineStyle == null) lineStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.UpperRight };
            return lineStyle;
        }

        private static float EstimateWidth(string[] lines)
        {
            int max = 0;
            foreach (string line in lines) max = Mathf.Max(max, line.Length);
            return Mathf.Clamp(max * 7f + 20f, 200f, 5000f);
        }

        private static int CountLines(string text)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            int count = 1;
            for (int i = 0; i < text.Length; i++) if (text[i] == '\n') count++;
            return count;
        }

        private static string Escape(string value)
        {
            return (value ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static string HighlightJson(string line)
        {
            string escaped = Escape(line);
            return Regex.Replace(
                escaped,
                "\"(?:\\\\.|[^\"\\\\])*\"|\\b(?:true|false|null)\\b|-?\\d+(?:\\.\\d+)?(?:[eE][+-]?\\d+)?",
                match =>
                {
                    if (match.Value.StartsWith("\"", StringComparison.Ordinal))
                    {
                        int next = match.Index + match.Length;
                        while (next < escaped.Length && char.IsWhiteSpace(escaped[next])) next++;
                        string color = next < escaped.Length && escaped[next] == ':' ? "#80CBC4" : "#FFD180";
                        return "<color=" + color + ">" + match.Value + "</color>";
                    }
                    if (match.Value == "true" || match.Value == "false" || match.Value == "null")
                        return "<color=#CE93D8>" + match.Value + "</color>";
                    return "<color=#A5D6A7>" + match.Value + "</color>";
                });
        }

        private enum JsonNodeKind { Object, Array, String, Number, Boolean, Null }
        private sealed class JsonNode
        {
            public JsonNodeKind Kind;
            public string Value;
            public readonly List<KeyValuePair<string, JsonNode>> Children = new List<KeyValuePair<string, JsonNode>>();
        }

        private sealed class JsonParser
        {
            private readonly string text;
            private int index;
            public JsonParser(string text) { this.text = text ?? string.Empty; }
            public JsonNode Parse()
            {
                Skip(); JsonNode result = ReadValue(); Skip();
                if (index != text.Length) throw new FormatException("Unexpected content at character " + index);
                return result;
            }
            private JsonNode ReadValue()
            {
                Skip();
                if (index >= text.Length) throw new FormatException("Unexpected end of JSON");
                char c = text[index];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return new JsonNode { Kind = JsonNodeKind.String, Value = ReadString() };
                if (c == 't' || c == 'f') return ReadBoolean();
                if (c == 'n') { ExpectWord("null"); return new JsonNode { Kind = JsonNodeKind.Null, Value = "null" }; }
                return ReadNumber();
            }
            private JsonNode ReadObject()
            {
                Expect('{'); JsonNode node = new JsonNode { Kind = JsonNodeKind.Object }; Skip();
                if (Try('}')) return node;
                while (true)
                {
                    Skip(); string key = ReadString(); Skip(); Expect(':');
                    node.Children.Add(new KeyValuePair<string, JsonNode>(key, ReadValue())); Skip();
                    if (Try('}')) break; Expect(',');
                }
                return node;
            }
            private JsonNode ReadArray()
            {
                Expect('['); JsonNode node = new JsonNode { Kind = JsonNodeKind.Array }; Skip();
                if (Try(']')) return node;
                int i = 0;
                while (true)
                {
                    node.Children.Add(new KeyValuePair<string, JsonNode>("[" + i++ + "]", ReadValue())); Skip();
                    if (Try(']')) break; Expect(',');
                }
                return node;
            }
            private JsonNode ReadBoolean()
            {
                if (Peek("true")) { ExpectWord("true"); return new JsonNode { Kind = JsonNodeKind.Boolean, Value = "true" }; }
                ExpectWord("false"); return new JsonNode { Kind = JsonNodeKind.Boolean, Value = "false" };
            }
            private JsonNode ReadNumber()
            {
                int start = index;
                if (Try('-')) { }
                while (index < text.Length && char.IsDigit(text[index])) index++;
                if (Try('.')) while (index < text.Length && char.IsDigit(text[index])) index++;
                if (index < text.Length && (text[index] == 'e' || text[index] == 'E')) { index++; if (index < text.Length && (text[index] == '+' || text[index] == '-')) index++; while (index < text.Length && char.IsDigit(text[index])) index++; }
                if (start == index) throw new FormatException("Invalid value at character " + index);
                return new JsonNode { Kind = JsonNodeKind.Number, Value = text.Substring(start, index - start) };
            }
            private string ReadString()
            {
                Expect('"'); StringBuilder b = new StringBuilder();
                while (index < text.Length)
                {
                    char c = text[index++];
                    if (c == '"') return b.ToString();
                    if (c == '\\')
                    {
                        if (index >= text.Length) break;
                        char e = text[index++];
                        switch (e)
                        {
                            case '"': b.Append('"'); break; case '\\': b.Append('\\'); break; case '/': b.Append('/'); break;
                            case 'b': b.Append('\b'); break; case 'f': b.Append('\f'); break; case 'n': b.Append('\n'); break; case 'r': b.Append('\r'); break; case 't': b.Append('\t'); break;
                            case 'u': if (index + 4 > text.Length) throw new FormatException("Invalid unicode escape"); b.Append((char)int.Parse(text.Substring(index, 4), NumberStyles.HexNumber)); index += 4; break;
                            default: throw new FormatException("Invalid escape at character " + index);
                        }
                    }
                    else b.Append(c);
                }
                throw new FormatException("Unterminated string");
            }
            private void Skip() { while (index < text.Length && char.IsWhiteSpace(text[index])) index++; }
            private bool Try(char c) { Skip(); if (index < text.Length && text[index] == c) { index++; return true; } return false; }
            private void Expect(char c) { if (!Try(c)) throw new FormatException("Expected '" + c + "' at character " + index); }
            private bool Peek(string word) { return string.Compare(text, index, word, 0, word.Length, StringComparison.Ordinal) == 0; }
            private void ExpectWord(string word) { if (!Peek(word)) throw new FormatException("Expected " + word + " at character " + index); index += word.Length; }
        }

        private static class JsonFormatter
        {
            public static string Format(string input)
            {
                StringBuilder output = new StringBuilder(); int indent = 0; bool quoted = false; bool escaped = false;
                for (int i = 0; i < input.Length; i++)
                {
                    char c = input[i];
                    if (quoted)
                    {
                        output.Append(c);
                        if (escaped) escaped = false; else if (c == '\\') escaped = true; else if (c == '"') quoted = false;
                        continue;
                    }
                    if (c == '"') { quoted = true; output.Append(c); }
                    else if (c == '{' || c == '[') { output.Append(c); output.AppendLine(); indent++; AppendIndent(output, indent); }
                    else if (c == '}' || c == ']') { output.AppendLine(); indent = Math.Max(0, indent - 1); AppendIndent(output, indent); output.Append(c); }
                    else if (c == ',') { output.Append(c); output.AppendLine(); AppendIndent(output, indent); }
                    else if (c == ':') output.Append(": ");
                    else if (!char.IsWhiteSpace(c)) output.Append(c);
                }
                return output.ToString();
            }
            private static void AppendIndent(StringBuilder b, int indent) { b.Append(' ', indent * 4); }
        }
    }

    internal sealed class UTLogContentViewerWindow : EditorWindow
    {
        private string content;
        private string viewerTitle;
        private UTLogContentViewer viewer;

        public static void Open(string title, string text)
        {
            string safeTitle = title ?? "Log Data";
            UTLogContentViewerWindow window = GetWindow<UTLogContentViewerWindow>(true, "Uni7 - " + safeTitle, true);
            window.minSize = new Vector2(520, 360);
            window.content = text ?? string.Empty;
            window.viewerTitle = safeTitle;
            window.viewer = new UTLogContentViewer();
            window.Show();
        }

        private void OnGUI()
        {
            if (viewer == null) viewer = new UTLogContentViewer();
            viewer.Draw(content ?? string.Empty, viewerTitle ?? "Log Data");
        }
    }

}
