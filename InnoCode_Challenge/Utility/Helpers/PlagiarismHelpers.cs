using System.Security.Cryptography;
using System.Text;
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

        public static string Sha256Hex(string input)
        {
            using var sha = SHA256.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = sha.ComputeHash(bytes);
            return Convert.ToHexString(hash); // .NET 5+
        }
    }

}
