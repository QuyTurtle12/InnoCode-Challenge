using System.Security.Cryptography;
using System.Text;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Utility.Helpers
{
    public static class PlagiarismHelpers
    {
        // Remove python single-line comments
        private static readonly Regex CommentRegex = new(@"(?m)#.*$", RegexOptions.Compiled);

        // Remove triple-quoted blocks (often docstrings).
        private static readonly Regex TripleQuotedRegex = new(@"(?s)('''.*?'''|\""\"".*?\""\"")", RegexOptions.Compiled);

        private static readonly Regex WhitespaceRegex = new(@"\s+", RegexOptions.Compiled);

        private static readonly HashSet<string> PythonKeywords = new(StringComparer.Ordinal)
        {
            "False", "None", "True", "and", "as", "assert", "async", "await", "break",
            "class", "continue", "def", "del", "elif", "else", "except", "finally",
            "for", "from", "global", "if", "import", "in", "is", "lambda", "nonlocal",
            "not", "or", "pass", "raise", "return", "try", "while", "with", "yield",
            "match", "case"
        };

        public static string NormalizePython(string code, bool removeTripleQuoted = true)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;

            code = code.Replace("\r\n", "\n");

            if (removeTripleQuoted)
                code = TripleQuotedRegex.Replace(code, "");

            code = CommentRegex.Replace(code, "");

            // Remove ALL whitespace
            code = WhitespaceRegex.Replace(code, "");

            return code.Trim();
        }

        public static string NormalizePythonForFingerprint(
            string code,
            bool removeTripleQuoted = true,
            bool normalizeIdentifiers = true)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;

            code = code.Replace("\r\n", "\n");

            if (removeTripleQuoted)
                code = TripleQuotedRegex.Replace(code, "");

            code = CommentRegex.Replace(code, "");

            if (normalizeIdentifiers)
                code = NormalizePythonIdentifiers(code);

            code = WhitespaceRegex.Replace(code, "");

            return code.Trim();
        }

        public static string Sha256Hex(string input)
        {
            using var sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash); // .NET 5+
        }

        private static string NormalizePythonIdentifiers(string code)
        {
            var sb = new StringBuilder(code.Length);
            var identifierMap = new Dictionary<string, string>(StringComparer.Ordinal);
            int nextId = 1;

            for (int i = 0; i < code.Length;)
            {
                char c = code[i];

                if (c == '\'' || c == '"')
                {
                    int newIndex = SkipStringLiteral(code, i);
                    sb.Append(code, i, newIndex - i);
                    i = newIndex;
                    continue;
                }

                if (IsIdentifierStart(c))
                {
                    int start = i;
                    i++;
                    while (i < code.Length && IsIdentifierPart(code[i]))
                    {
                        i++;
                    }

                    string ident = code.Substring(start, i - start);
                    if (PythonKeywords.Contains(ident))
                    {
                        sb.Append(ident);
                    }
                    else
                    {
                        if (!identifierMap.TryGetValue(ident, out string? replacement))
                        {
                            replacement = $"v{nextId++}";
                            identifierMap[ident] = replacement;
                        }
                        sb.Append(replacement);
                    }

                    continue;
                }

                sb.Append(c);
                i++;
            }

            return sb.ToString();
        }

        private static int SkipStringLiteral(string code, int startIndex)
        {
            if (startIndex < 0 || startIndex >= code.Length) return code.Length;

            char quote = code[startIndex];
            bool isTriple = startIndex + 2 < code.Length
                            && code[startIndex + 1] == quote
                            && code[startIndex + 2] == quote;

            if (isTriple)
            {
                string marker = new string(quote, 3);
                int endIndex = code.IndexOf(marker, startIndex + 3, StringComparison.Ordinal);
                return endIndex < 0 ? code.Length : endIndex + 3;
            }

            int i = startIndex + 1;
            while (i < code.Length)
            {
                if (code[i] == '\\')
                {
                    i += 2;
                    continue;
                }

                if (code[i] == quote)
                {
                    i++;
                    break;
                }

                i++;
            }

            return i;
        }

        private static bool IsIdentifierStart(char c)
        {
            return char.IsLetter(c) || c == '_';
        }

        private static bool IsIdentifierPart(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }
    }

}
