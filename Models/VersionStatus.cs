namespace SimpleTarkovManager.Models
{
    /// <summary>
    /// Provides a structured status for the game installation check.
    /// </summary>
    public enum VersionStatus
    {
        /// <summary>
        /// The game version was successfully determined from the ConsistencyInfo manifest.
        /// </summary>
        OK,
        /// <summary>
        /// The game executable (EscapeFromTarkov.exe) is missing from the directory.
        /// </summary>
        ExecutableMissing,
        /// <summary>
        /// The ConsistencyInfo manifest is missing or corrupt, but the version was read from the EXE. Repair is needed.
        /// </summary>
        ManifestIsCorrupt,
        /// <summary>
        /// The versions in the EXE and the ConsistencyInfo manifest do not match.
        /// </summary>
        VersionMismatch,
        /// <summary>
        /// The installation is critically damaged. The manifest is missing and the game executable is unreadable or missing.
        /// </summary>
        CriticallyCorrupt
    }
}