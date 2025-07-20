namespace Juice.Storage
{
    public class UploadOptions
    {
        public long SectionSize { get; set; } = 10485760;

        public bool DeleteOnAbort { get; set; }

        public bool PreserveDateModified { get; set; }

        private static long? _serverMaxBodySize;
        private static readonly object _lock = new object();
        public static long? ServerMaxBodySizeFromHandledError
        {
            get
            {
                lock (_lock)
                {
                    return _serverMaxBodySize;
                }
            }
            set
            {
                lock (_lock)
                {
                    _serverMaxBodySize = value;
                }
            }
        }
    }
}
