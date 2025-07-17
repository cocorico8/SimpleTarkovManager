using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace SimpleTarkovManager.Models
{
    public struct EftVersion : IEquatable<EftVersion>, IComparable<EftVersion>
    {
        public byte Release { get; }
        public ushort Major { get; }
        public ushort Minor { get; }
        public ushort Hotfix { get; }
        public uint Build { get; }

        public EftVersion(byte release, ushort major, ushort minor, ushort hotfix, uint build)
        {
            Release = release; Major = major; Minor = minor; Hotfix = hotfix; Build = build;
        }

        public static bool TryParse(string? input, [NotNullWhen(true)] out EftVersion? result)
        {
            result = null;
            if (string.IsNullOrEmpty(input)) return false;
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
            catch { return false; }
        }
        
        public static bool TryFromFile(string filePath, [NotNullWhen(true)] out EftVersion? result)
        {
            result = null;
            try
            {
                var versionInfo = FileVersionInfo.GetVersionInfo(filePath);
                if (string.IsNullOrEmpty(versionInfo.ProductVersion))
                {
                    return false; // ProductVersion string is missing.
                }

                // Parse the string eg: "0.16.8.1-38114-1bb130aa"
                string[] parts = versionInfo.ProductVersion.Split('-');
                if (parts.Length < 2)
                {
                    return false; // The string is not in the expected "X-Y-Z" format.
                }

                // Reconstruct the full 5-part version string: "0.16.8.1.38114"
                string fullVersionString = $"{parts[0]}.{parts[1]}";
                
                // Use our existing string parser to create the EftVersion struct.
                return TryParse(fullVersionString, out result);
            }
            catch
            {
                return false;
            }
        }

        public override string ToString() => $"{Release}.{Major}.{Minor}.{Hotfix}.{Build}";
        public int CompareTo(EftVersion other)
        {
            if (Release.CompareTo(other.Release) != 0) return Release.CompareTo(other.Release);
            if (Major.CompareTo(other.Major) != 0) return Major.CompareTo(other.Major);
            if (Minor.CompareTo(other.Minor) != 0) return Minor.CompareTo(other.Minor);
            if (Hotfix.CompareTo(other.Hotfix) != 0) return Hotfix.CompareTo(other.Hotfix);
            return Build.CompareTo(other.Build);
        }
        public bool Equals(EftVersion other) => CompareTo(other) == 0;
        public override bool Equals(object? obj) => obj is EftVersion other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Release, Major, Minor, Hotfix, Build);
        public static bool operator ==(EftVersion left, EftVersion right) => left.Equals(right);
        public static bool operator !=(EftVersion left, EftVersion right) => !left.Equals(right);
        public static bool operator >(EftVersion left, EftVersion right) => left.CompareTo(right) > 0;
        public static bool operator <(EftVersion left, EftVersion right) => left.CompareTo(right) < 0;
        public static bool operator >=(EftVersion left, EftVersion right) => left.CompareTo(right) >= 0;
        public static bool operator <=(EftVersion left, EftVersion right) => left.CompareTo(right) <= 0;
    }
}