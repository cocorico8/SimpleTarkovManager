using System;
using System.Linq;
using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace SimpleTarkovManager.Services
{
    public class HardwareService
    {
        /// <summary>
        /// Generates the Hardware ID (HWID) in the exact format required by the EFT Launcher API.
        /// This logic is a direct C# port of the process found in the obfuscated launcher code.
        /// </summary>
        public string GenerateHwIdV1()
        {
            // Gather all necessary hardware information using WMI
            string biosManufacturer = GetProperty("Win32_BIOS", "Manufacturer");
            string biosName = GetProperty("Win32_BIOS", "Name");
            string biosSerial = GetProperty("Win32_BIOS", "SerialNumber");

            var baseboard = GetManagementObject("Win32_BaseBoard", "Manufacturer,Name,Product,SerialNumber");
            string baseboardManufacturer = baseboard?["Manufacturer"]?.ToString() ?? "";
            string baseboardProduct = baseboard?["Product"]?.ToString() ?? "";
            string baseboardSerial = baseboard?["SerialNumber"]?.ToString() ?? "";
            
            var cpu = GetManagementObject("Win32_Processor", "Manufacturer,Name,SerialNumber,UniqueId");
            string cpuManufacturer = cpu?["Manufacturer"]?.ToString() ?? "";
            string cpuName = cpu?["Name"]?.ToString() ?? "";
            string cpuSerial = cpu?["SerialNumber"]?.ToString() ?? "";
            string cpuUniqueId = cpu?["UniqueId"]?.ToString() ?? "";

            string osManufacturer = GetProperty("Win32_OperatingSystem", "Manufacturer");
            string osSerial = GetProperty("Win32_OperatingSystem", "SerialNumber");

            // 1. Create SHA1 hashes for individual components
            string biosHash = GetSha1Hex(biosManufacturer + biosName + biosSerial);
            string baseboardHash = GetSha1Hex(baseboardManufacturer + baseboard.Properties["Name"].Value + baseboardProduct + baseboardSerial);
            string cpuHash = GetSha1Hex(cpuManufacturer + cpuName + cpuSerial + cpuUniqueId);
            string osHash = GetSha1Hex(osManufacturer + osSerial);

            // 2. Create the special combined hash (SHA1 -> MD5)
            string specialHashInput = baseboardSerial + biosSerial + cpuUniqueId + osSerial;
            string sha1OfSpecial = GetSha1Hex(specialHashInput);
            string specialHash = GetMd5Hex(sha1OfSpecial);
            
            // 3. Assemble the final HWID string
            string[] hwIdParts = new string[7];
            
            // A unix timestamp divided by 1,000,000. Not used in final hash calculation but part of the string to be hashed.
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 1000000L;

            hwIdParts[0] = "#1";
            // Index 1 is calculated last
            hwIdParts[2] = biosHash;
            hwIdParts[3] = baseboardHash;
            hwIdParts[4] = cpuHash;
            hwIdParts[5] = osHash;
            hwIdParts[6] = specialHash;

            string finalHashInput = timestamp.ToString() + string.Concat(hwIdParts);
            hwIdParts[1] = GetSha1Hex(finalHashInput);

            return string.Join("-", hwIdParts);
        }
        
        private string GetProperty(string wmiClass, string propertyName)
        {
            try
            {
                var searcher = new ManagementObjectSearcher($"SELECT {propertyName} FROM {wmiClass}");
                var obj = searcher.Get().OfType<ManagementObject>().FirstOrDefault();
                return obj?[propertyName]?.ToString() ?? "";
            }
            catch { return ""; }
        }

        private ManagementObject GetManagementObject(string wmiClass, string properties)
        {
            try
            {
                var searcher = new ManagementObjectSearcher($"SELECT {properties} FROM {wmiClass}");
                return searcher.Get().OfType<ManagementObject>().FirstOrDefault();
            }
            catch { return null; }
        }

        private string GetSha1Hex(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            using (SHA1 sha1 = SHA1.Create())
            {
                byte[] hashBytes = sha1.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder(hashBytes.Length * 2);
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }

        private string GetMd5Hex(string input)
        {
            if (string.IsNullOrEmpty(input)) return "";
            using (MD5 md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                var sb = new StringBuilder(hashBytes.Length * 2);
                foreach (byte b in hashBytes)
                {
                    sb.Append(b.ToString("x2"));
                }
                return sb.ToString();
            }
        }
    }
}