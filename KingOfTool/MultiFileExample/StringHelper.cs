using System.Linq;

namespace MultiFileExample
{
    public static class StringHelper
    {
        public static string Reverse(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return new string(input.Reverse().ToArray());
        }

        public static bool IsPalindrome(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            string cleaned = input.Replace(" ", "").ToLower();
            return cleaned == Reverse(cleaned);
        }

        public static int CountWords(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return 0;

            return input.Split(new[] { ' ', '\t', '\n', '\r' },
                System.StringSplitOptions.RemoveEmptyEntries).Length;
        }
    }
}