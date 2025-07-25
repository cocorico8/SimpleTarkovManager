﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using System.Linq;
using System.Threading.Tasks;

namespace SimpleTarkovManager.Services
{
    public class DialogService
    {
        public async Task<string?> OpenFolderAsync()
        {
            // Get the main window dynamically at the moment it's needed.
            var mainWindow = GetMainWindow();
            if (mainWindow == null)
            {
                // If we can't find the main window, we can't show the dialog.
                return null;
            }

            var result = await mainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = "Select Escape from Tarkov Directory",
                AllowMultiple = false
            });

            // The result is an array of folders, we just need the first one.
            return result.FirstOrDefault()?.Path.LocalPath;
        }

        private Window? GetMainWindow()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                return desktop.MainWindow;
            }
            return null;
        }
    }
}