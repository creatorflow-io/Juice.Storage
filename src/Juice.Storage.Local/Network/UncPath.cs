namespace Juice.Storage.Local
{
    /// <summary>
    /// Helpers for network share paths: \\server\share\dir or smb://server/share/dir
    /// (and //server/share/dir on Windows).
    /// </summary>
    public static class UncPath
    {
        private const string SmbScheme = "smb://";
        private static readonly char[] _separators = new[] { '\\', '/' };

        public static bool IsNetworkPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }
            return path.StartsWith(@"\\", StringComparison.Ordinal)
                || path.StartsWith(SmbScheme, StringComparison.OrdinalIgnoreCase)
                // On Unix, //dir is a valid local path
                || (OperatingSystem.IsWindows() && path.StartsWith("//", StringComparison.Ordinal));
        }

        /// <summary>
        /// Splits a network path into server, share and the path inside the share (separated by '/').
        /// </summary>
        public static bool TryParse(string? path, out string server, out string share, out string subPath)
        {
            server = share = subPath = "";
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string rest;
            if (path.StartsWith(SmbScheme, StringComparison.OrdinalIgnoreCase))
            {
                rest = path.Substring(SmbScheme.Length);
            }
            else if (path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal))
            {
                rest = path.Substring(2);
            }
            else
            {
                return false;
            }

            var segments = rest.Split(_separators, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || segments.Any(s => s == "." || s == ".."))
            {
                return false;
            }
            server = segments[0];
            share = segments.Length > 1 ? segments[1] : "";
            subPath = string.Join("/", segments.Skip(2));
            return true;
        }

        /// <summary>
        /// Converts a network path to Windows UNC form: \\server\share\dir
        /// </summary>
        public static string ToWindowsUnc(string path)
        {
            if (!TryParse(path, out var server, out var share, out var subPath))
            {
                return path;
            }
            var result = @"\\" + server;
            if (!string.IsNullOrEmpty(share))
            {
                result += @"\" + share;
            }
            if (!string.IsNullOrEmpty(subPath))
            {
                result += @"\" + subPath.Replace('/', '\\');
            }
            return result;
        }
    }
}
