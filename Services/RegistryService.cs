using Microsoft.Win32;
using System;
using System.IO;
using SimpleTarkovManager.Models;

namespace SimpleTarkovManager.Services
{
    public class RegistryService
    {
        // We use HKEY_CURRENT_USER to avoid needing admin rights.
        private const string RegistryKeyPath = @"SOFTWARE\SimpleEFTLauncher";

        public string? GetInstallPath()
        {
            try
            {
                // Open the key for the current user.
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                
                // Retrieve the value associated with "InstallPath".
                // If the key or value doesn't exist, it will return null.
                return key?.GetValue("InstallPath") as string;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error reading from registry: {ex.Message}");
                return null;
            }
        }
        
        public void WriteOfficialRegistryKey(string installPath, EftVersion version)
        {
            // This is the official Uninstall key used by BSG.
            const string keyPath = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\EscapeFromTarkov";

            // This will throw an UnauthorizedAccessException if the launcher is not run as admin.
            using var key = Registry.LocalMachine.CreateSubKey(keyPath);

            if (key != null)
            {
                key.SetValue("DisplayIcon", Path.Combine(installPath, "EscapeFromTarkov.exe"));
                key.SetValue("DisplayName", "Escape from Tarkov");
                key.SetValue("DisplayVersion", version.ToString());
                key.SetValue("InstallLocation", installPath);
                key.SetValue("Publisher", "Battlestate Games Limited");
            }
            else
            {
                throw new Exception("Could not create or open the required registry key.");
            }
        }

        public void SetInstallPath(string installPath, string version)
        {
            try
            {
                // Create or open the key with write access.
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);

                if (key != null)
                {
                    // Set the values. This is similar to the official launcher's behavior.
                    key.SetValue("InstallPath", installPath);
                    key.SetValue("DisplayVersion", version);
                    key.SetValue("DisplayName", "Escape From Tarkov (Simple Launcher)");
                }
            }
            catch (Exception ex)
            {
                // This could fail if there are permission issues, though unlikely with HKCU.
                Console.WriteLine($"Error writing to registry: {ex.Message}");
            }
        }
    }
}