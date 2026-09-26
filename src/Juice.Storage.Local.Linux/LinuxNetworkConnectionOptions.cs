namespace Juice.Storage.Local.Linux
{
    public class LinuxNetworkConnectionOptions
    {
        /// <summary>
        /// Shares are mounted at {MountRoot}/{server}/{share} (lower case).
        /// <para>Shares that are already mounted there (e.g. by /etc/fstab) are used as is.</para>
        /// </summary>
        public string MountRoot { get; set; } = "/mnt/juice-storage";

        /// <summary>
        /// Additional mount.cifs options, comma separated. Ex: "vers=3.0,sec=ntlmssp"
        /// </summary>
        public string? MountOptions { get; set; }

        /// <summary>
        /// Owner of the mounted files. Default: the uid of the current process.
        /// </summary>
        public int? Uid { get; set; }

        /// <summary>
        /// Group of the mounted files. Default: the gid of the current process.
        /// </summary>
        public int? Gid { get; set; }

        public string FileMode { get; set; } = "0660";
        public string DirMode { get; set; } = "0770";

        /// <summary>
        /// Run mount/umount through "sudo -n" when the process is not root.
        /// Requires a NOPASSWD sudoers rule for mount, umount and mkdir.
        /// </summary>
        public bool UseSudo { get; set; }

        public string MountCommand { get; set; } = "mount";
        public string UnmountCommand { get; set; } = "umount";
        public string SudoCommand { get; set; } = "sudo";

        public TimeSpan CommandTimeout { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Unmount the shares mounted by this process when the application stops.
        /// </summary>
        public bool UnmountOnDispose { get; set; } = true;
    }
}
