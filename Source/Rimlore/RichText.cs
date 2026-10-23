using System.Text.RegularExpressions;

namespace ColonyChronicle
{
    public static class RichText
    {
        private static readonly Regex Tags = new Regex("<[^>]+>", RegexOptions.Compiled);

        public static string Strip(string s) => string.IsNullOrEmpty(s) ? s : Tags.Replace(s, "").Trim();
    }
}
