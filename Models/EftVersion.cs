using System;
using System.Diagnostics;
using System.Linq;

namespace SimpleTarkovManager.Models
{
    /// <summary>
    /// A custom version struct based on the official BsgVersion.
    /// It correctly handles 4-part and 5-part version strings.
    /// </summary>
    public struct EftVersion : IEquatable<EftVersion>, IComparable<EftVersion>
    {
        public byte Release { get; }
        public ushort Major { get; }
        public ushort Minor { get; }
        public ushort Hotfix { get; }
        public uint Build { get; }

        public EftVersion(byte release, ushort major, ushort minor, ushort hotfix, uint build)
        {
            Release = release;
            Major = major;
            Minor = minor;
            Hotfix = hotfix;
            Build = build;
        }

        public static bool TryParse(string? input, out EftVersion result)
        {
            result = default;
            if (string.IsNullOrEmpty(input))
                return false;

            try
            {
                var parts = input.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 4)
                {
                    result = new EftVersion(byte.Parse(parts[0]), ushort.Parse(parts[1]), ushort.Parse(parts[2]), 0, uint.Parse(parts[3]));
                    return true;
                }
                if (parts.Length == 5)
                {
                    result = new EftVersion(byte.Parse(parts[0]), ushort.Parse(parts[1]), ushort.Parse(parts[2]), ushort.Parse(parts[3]), uint.Parse(parts[4]));
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryFromFile(string filePath, out EftVersion result)
        {
            result = default;
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
                // The official launcher tries to parse ProductVersion first, which can be complex.
                // A simpler, more reliable approach for modern EFT is to use the direct file version parts.
                if (versionInfo.FilePrivatePart > 0)
                {
                    result = new EftVersion((byte)versionInfo.FileMajorPart, (ushort)versionInfo.FileMinorPart, (ushort)versionInfo.FileBuildPart, 0, (uint)versionInfo.FilePrivatePart);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        public override string ToString()
        {
            return $"{Release}.{Major}.{Minor}.{Hotfix}.{Build}";
        }

        // --- Comparison and Equality Logic ---
        public bool Equals(EftVersion other) => CompareTo(other) == 0;
        public override bool Equals(object? obj) => obj is EftVersion other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Release, Major, Minor, Hotfix, Build);
        public int CompareTo(EftVersion other)
        {
            if (Release.CompareTo(other.Release) != 0) return Release.CompareTo(other.Release);
            if (Major.CompareTo(other.Major) != 0) return Major.CompareTo(other.Major);
            if (Minor.CompareTo(other.Minor) != 0) return Minor.CompareTo(other.Minor);
            if (Hotfix.CompareTo(other.Hotfix) != 0) return Hotfix.CompareTo(other.Hotfix);
            return Build.CompareTo(other.Build);
        }

        public static bool operator ==(EftVersion left, EftVersion right) => left.Equals(right);
        public static bool operator !=(EftVersion left, EftVersion right) => !left.Equals(right);
        public static bool operator >(EftVersion left, EftVersion right) => left.CompareTo(right) > 0;
        public static bool operator <(EftVersion left, EftVersion right) => left.CompareTo(right) < 0;
        public static bool operator >=(EftVersion left, EftVersion right) => left.CompareTo(right) >= 0;
        public static bool operator <=(EftVersion left, EftVersion right) => left.CompareTo(right) <= 0;
    }
}