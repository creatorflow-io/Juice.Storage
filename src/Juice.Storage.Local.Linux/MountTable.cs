using System.Text;

namespace Juice.Storage.Local.Linux
{
    /// <summary>
    /// Reads mount points from /proc/self/mounts.
    /// </summary>
    internal static class MountTable
    {
        public const string DefaultPath = "/proc/self/mounts";

        public static string Read() => File.ReadAllText(DefaultPath);

        public static bool IsMountPoint(string mountsContent, string path)
        {
            var target = TrimEndSeparator(path);
            foreach (var line in mountsContent.Split('\n'))
            {
                // <source> <mount point> <fs type> <options> <dump> <pass>
                var fields = line.Split(' ');
                if (fields.Length < 2)
                {
                    continue;
                }
                if (string.Equals(TrimEndSeparator(Unescape(fields[1])), target, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Mount points escape space, tab, newline and backslash as octal: \040 \011 \012 \134
        /// </summary>
        internal static string Unescape(string value)
        {
            if (value.IndexOf('\\') < 0)
            {
                return value;
            }
            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 3 < value.Length
                    && IsOctal(value[i + 1]) && IsOctal(value[i + 2]) && IsOctal(value[i + 3]))
                {
                    builder.Append((char)Convert.ToInt32(value.Substring(i + 1, 3), 8));
                    i += 3;
                }
                else
                {
                    builder.Append(value[i]);
                }
            }
            return builder.ToString();
        }

        private static bool IsOctal(char c) => c >= '0' && c <= '7';

        private static string TrimEndSeparator(string path)
            => path.Length > 1 ? path.TrimEnd('/') : path;
    }
}
