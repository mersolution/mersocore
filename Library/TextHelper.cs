using System.Text.RegularExpressions;

namespace mersolutionCore.Library
{
    /// <summary>
    /// Text manipulation and sanitization utilities
    /// </summary>
    public static class TextHelper
    {
        /// <summary>
        /// Clear SQL injection patterns from text
        /// </summary>
        /// <param name="text">Text to sanitize</param>
        /// <returns>Sanitized text</returns>
        public static string ClearSqlInjection(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            text = text.Replace("'", "''");
            text = text.Replace("--", "");
            text = text.Replace("/*", "");
            text = text.Replace("*/", "");
            text = text.Replace(";", "");
            text = Regex.Replace(text, @"\bdrop\b", "drp", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\balter\b", "atr", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bdelete\b", "dl", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\btruncate\b", "trnc", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bexec\b", "exc", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"\bexecute\b", "exc", RegexOptions.IgnoreCase);

            return text;
        }

        /// <summary>
        /// Remove HTML tags from text
        /// </summary>
        /// <param name="htmlText">HTML text</param>
        /// <returns>Plain text</returns>
        public static string ClearHtmlTags(string htmlText)
        {
            if (string.IsNullOrEmpty(htmlText))
                return string.Empty;

            var regex = new Regex("<[^>]+>", RegexOptions.IgnoreCase);
            return regex.Replace(htmlText, "");
        }

        /// <summary>
        /// Strip HTML and convert to plain text with formatting
        /// </summary>
        /// <param name="source">HTML source</param>
        /// <returns>Plain text</returns>
        public static string StripHtml(string source)
        {
            if (string.IsNullOrEmpty(source))
                return string.Empty;

            string result = source;

            result = result.Replace("\r", " ");
            result = result.Replace("\n", " ");
            result = result.Replace("\t", string.Empty);
            result = Regex.Replace(result, @"( )+", " ");

            // Remove head section
            result = Regex.Replace(result, @"<( )*head([^>])*>", "<head>", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"(<( )*(/)( )*head( )*>)", "</head>", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "(<head>).*(</head>)", string.Empty, RegexOptions.IgnoreCase);

            // Remove script section
            result = Regex.Replace(result, @"<( )*script([^>])*>", "<script>", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"(<( )*(/)( )*script( )*>)", "</script>", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"(<script>).*(</script>)", string.Empty, RegexOptions.IgnoreCase);

            // Remove style section
            result = Regex.Replace(result, @"<( )*style([^>])*>", "<style>", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"(<( )*(/)( )*style( )*>)", "</style>", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, "(<style>).*(</style>)", string.Empty, RegexOptions.IgnoreCase);

            // Convert formatting tags
            result = Regex.Replace(result, @"<( )*td([^>])*>", "\t", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<( )*br( )*(/)?( )*>", "\r", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<( )*li( )*>", "\r", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<( )*div([^>])*>", "\r\r", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<( )*tr([^>])*>", "\r\r", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<( )*p([^>])*>", "\r\r", RegexOptions.IgnoreCase);

            // Remove remaining tags
            result = Regex.Replace(result, @"<[^>]*>", string.Empty, RegexOptions.IgnoreCase);

            // Decode HTML entities
            result = Regex.Replace(result, @"&nbsp;", " ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"&bull;", " * ", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"&lsaquo;", "<", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"&rsaquo;", ">", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"&trade;", "(tm)", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"&frasl;", "/", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"&lt;", "<", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"&gt;", ">", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"&copy;", "(c)", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"&reg;", "(r)", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"&amp;", "&", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"&(.{2,6});", string.Empty, RegexOptions.IgnoreCase);

            // Clean up whitespace
            result = result.Replace("\n", "\r");
            result = Regex.Replace(result, @"(\r)( )+(\r)", "\r\r", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"(\r){3,}", "\r\r", RegexOptions.IgnoreCase);

            return result.Trim();
        }

        /// <summary>
        /// Truncate text to specified length
        /// </summary>
        /// <param name="text">Text to truncate</param>
        /// <param name="maxLength">Maximum length</param>
        /// <param name="suffix">Suffix to add (default: "...")</param>
        /// <returns>Truncated text</returns>
        public static string Truncate(string text, int maxLength, string suffix = "...")
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
                return text;

            return text.Substring(0, maxLength - suffix.Length) + suffix;
        }

        /// <summary>
        /// Convert string to URL-friendly slug
        /// </summary>
        /// <param name="text">Text to convert</param>
        /// <returns>URL slug</returns>
        public static string ToSlug(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            // Convert to lowercase
            text = text.ToLowerInvariant();

            // Replace Turkish characters
            text = text.Replace("ı", "i");
            text = text.Replace("ğ", "g");
            text = text.Replace("ü", "u");
            text = text.Replace("ş", "s");
            text = text.Replace("ö", "o");
            text = text.Replace("ç", "c");

            // Remove invalid characters
            text = Regex.Replace(text, @"[^a-z0-9\s-]", "");

            // Convert spaces to hyphens
            text = Regex.Replace(text, @"\s+", "-");

            // Remove multiple hyphens
            text = Regex.Replace(text, @"-+", "-");

            // Trim hyphens
            text = text.Trim('-');

            return text;
        }

        /// <summary>
        /// Check if string is valid email format
        /// </summary>
        /// <param name="email">Email to validate</param>
        /// <returns>True if valid email</returns>
        public static bool IsValidEmail(string email)
        {
            if (string.IsNullOrEmpty(email))
                return false;

            string pattern = @"^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$";
            return Regex.IsMatch(email, pattern);
        }

        /// <summary>
        /// Check if string is valid URL format
        /// </summary>
        /// <param name="url">URL to validate</param>
        /// <returns>True if valid URL</returns>
        public static bool IsValidUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
                return false;

            string pattern = @"^(https?:\/\/)?([\da-z\.-]+)\.([a-z\.]{2,6})([\/\w \.-]*)*\/?$";
            return Regex.IsMatch(url, pattern, RegexOptions.IgnoreCase);
        }
    }
}
