using System.Text.RegularExpressions;

namespace RG.SSR.JavaScript
{
    internal static class ModuleSyntaxDetector
    {
        // Pattern to match single-line comments, multi-line comments, single-quoted strings,
        // double-quoted strings, and template literals
        private static readonly Regex StripPattern = new(
            @"//[^\n]*" +                   // single-line comments
            @"|/\*[\s\S]*?\*/" +            // multi-line comments
            @"|'(?:[^'\\]|\\.)*'" +         // single-quoted strings
            @"|""(?:[^""\\]|\\.)*""" +      // double-quoted strings
            @"|`(?:[^`\\]|\\.)*`",          // template literals
            RegexOptions.Compiled
        );

        // Pattern to match import statements: import followed by whitespace, {, ", or '
        private static readonly Regex ImportPattern = new(
            @"\bimport\b\s*(?:[{""']|\s)",
            RegexOptions.Compiled
        );

        // Pattern to match export statements: export followed by whitespace then default, function, class, const, let, var, or {
        private static readonly Regex ExportPattern = new(
            @"\bexport\s+(default|function|class|const|let|var)\b|\bexport\s*\{",
            RegexOptions.Compiled
        );

        /// <summary>
        /// Determines whether the given JavaScript source contains ES module syntax
        /// (import/export statements) outside of comments and string literals.
        /// </summary>
        public static bool ContainsModuleSyntax(string source)
        {
            if (string.IsNullOrEmpty(source))
            {
                return false;
            }

            // Strip comments and string literals to avoid false positives
            string stripped = StripPattern.Replace(source, " ");

            return ImportPattern.IsMatch(stripped) || ExportPattern.IsMatch(stripped);
        }
    }
}
