using System.Collections.Generic;

namespace SimpleTarkovManager.Models
{
    // This class will hold all the hardware information, ready to be converted to JSON.
    public class SystemInfo
    {
        public int Version { get; } = 2;
        public string MachineName { get; set; }
        public string Checksum { get; set; } // handle this if needed later
        public BaseboardInfo Baseboard { get; set; }
        public BiosInfo Bios { get; set; }
        public List<CpuInfo> Cpu { get; set; }
        public List<GpuInfo> Gpu { get; set; }
        public OsInfo Os { get; set; }
        // Add other properties like RAM, Storage, etc., later if needed.
    }

    public class BaseboardInfo
    {
        public string Manufacturer { get; set; }
        public string Name { get; set; }
        public string Product { get; set; }
        public string SerialNumber { get; set; }
    }

    public class BiosInfo
    {
        public string Manufacturer { get; set; }
        public string Name { get; set; }
        public string SerialNumber { get; set; }
    }

    public class CpuInfo
    {
        public string Name { get; set; }
        public string Manufacturer { get; set; }
        public string SerialNumber { get; set; }
        public string UniqueId { get; set; }
    }

    public class GpuInfo
    {
        public string Name { get; set; }
        public string DriverVersion { get; set; }
        public uint AdapterRamGb { get; set; }
    }

    public class OsInfo
    {
        public string Manufacturer { get; set; }
        public string Caption { get; set; }
        public string SerialNumber { get; set; }
        public string Version { get; set; }
    }
}