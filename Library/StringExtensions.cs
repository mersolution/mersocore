namespace mersolutionCore.Library
{
    /// <summary>
    /// String extension methods
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Clear SQL injection patterns from string
        /// </summary>
        public static string ClearSqlInjection(this string text)
        {
            return TextHelper.ClearSqlInjection(text);
        }

        /// <summary>
        /// Remove HTML tags from string
        /// </summary>
        public static string ClearHtmlTags(this string text)
        {
            return TextHelper.ClearHtmlTags(text);
        }

        /// <summary>
        /// Strip HTML and convert to plain text
        /// </summary>
        public static string StripHtml(this string text)
        {
            return TextHelper.StripHtml(text);
        }

        /// <summary>
        /// Truncate string to specified length
        /// </summary>
        public static string Truncate(this string text, int maxLength, string suffix = "...")
        {
            return TextHelper.Truncate(text, maxLength, suffix);
        }

        /// <summary>
        /// Convert string to URL-friendly slug
        /// </summary>
        public static string ToSlug(this string text)
        {
            return TextHelper.ToSlug(text);
        }

        /// <summary>
        /// Check if string is valid email
        /// </summary>
        public static bool IsValidEmail(this string email)
        {
            return TextHelper.IsValidEmail(email);
        }

        /// <summary>
        /// Check if string is valid URL
        /// </summary>
        public static bool IsValidUrl(this string url)
        {
            return TextHelper.IsValidUrl(url);
        }

        /// <summary>
        /// Check if string is null or empty
        /// </summary>
        public static bool IsNullOrEmpty(this string text)
        {
            return string.IsNullOrEmpty(text);
        }

        /// <summary>
        /// Check if string is null or whitespace
        /// </summary>
        public static bool IsNullOrWhiteSpace(this string text)
        {
            return string.IsNullOrWhiteSpace(text);
        }

        /// <summary>
        /// Return default value if string is null or empty
        /// </summary>
        public static string DefaultIfEmpty(this string text, string defaultValue)
        {
            return string.IsNullOrEmpty(text) ? defaultValue : text;
        }
    }
}
