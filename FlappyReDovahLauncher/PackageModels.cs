using System.Collections.Generic;

namespace FlappyReDovahLauncher
{
    internal class PackageUnit
    {
        public string id { get; set; }
        public string kind { get; set; }
        public string path { get; set; }
        public string package { get; set; }
        public long packageSize { get; set; }
        public long totalSize { get; set; }
        public int fileCount { get; set; }
        public string fingerprint { get; set; }
        /// <summary>SHA-256 of the .7z package (hex). Optional for old indexes.</summary>
        public string packageSha256 { get; set; }
    }

    internal class PackageIndex
    {
        public int format { get; set; }
        public string version { get; set; }
        public string gameRoot { get; set; }
        public List<PackageUnit> units { get; set; }
    }

    /// <summary>Install channel: AE-only or full AE+VR.</summary>
    internal enum InstallChannel
    {
        AeOnly = 0,
        AeAndVr = 1
    }

    internal enum PlayButtonState
    {
        Install,
        Update,
        Play,
        Busy
    }
}
