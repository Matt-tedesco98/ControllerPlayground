using System;
using System.IO;
using System.Text;

namespace ControllerPlayground.Services;

internal static class VdfParser {
    public static VdfNode ParseFile(string path) {
        string text = File.ReadAllText(path);
        int position = 0;

        VdfNode root = new();

        ParseChildren(root, text, ref position);

        return root;
    }

    private static void ParseChildren(
        VdfNode parent,
        string text,
        ref int position) {
        while (position < text.Length) {
            SkipWhitespaceAndComments(text, ref position);

            if (position >= text.Length)
                return;

            if (text[position] == '}') {
                position++;
                return;
            }

            string? key = ReadToken(text, ref position);

            if (string.IsNullOrEmpty(key))
                return;

            SkipWhitespaceAndComments(text, ref position);

            if (position < text.Length && text[position] == '{') {
                position++;

                VdfNode child = new();

                ParseChildren(child, text, ref position);

                parent.Children[key] = child;
            } else {
                string? value = ReadToken(text, ref position);

                parent.Children[key] = new VdfNode {
                    Value = value
                };
            }
        }
    }

    private static string? ReadToken(
        string text,
        ref int position) {
        SkipWhitespaceAndComments(text, ref position);

        if (position >= text.Length)
            return null;

        if (text[position] == '"')
            return ReadQuotedToken(text, ref position);

        int start = position;

        while (position < text.Length &&
               !char.IsWhiteSpace(text[position]) &&
               text[position] != '{' &&
               text[position] != '}') {
            position++;
        }

        return text[start..position];
    }

    private static string ReadQuotedToken(
        string text,
        ref int position) {
        position++; // opening quote

        StringBuilder builder = new();

        while (position < text.Length) {
            char current = text[position++];

            if (current == '"')
                break;

            if (current == '\\' && position < text.Length) {
                char escaped = text[position++];

                builder.Append(escaped switch {
                    'n' => '\n',
                    'r' => '\r',
                    't' => '\t',
                    _ => escaped
                });

                continue;
            }

            builder.Append(current);
        }

        return builder.ToString();
    }

    private static void SkipWhitespaceAndComments(
        string text,
        ref int position) {
        while (position < text.Length) {
            if (char.IsWhiteSpace(text[position])) {
                position++;
                continue;
            }

            if (position + 1 < text.Length &&
                text[position] == '/' &&
                text[position + 1] == '/') {
                position += 2;

                while (position < text.Length &&
                       text[position] != '\n') {
                    position++;
                }

                continue;
            }

            break;
        }
    }
}