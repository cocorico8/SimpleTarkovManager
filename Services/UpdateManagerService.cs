using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SimpleTarkovManager.Models;

namespace SimpleTarkovManager.Services
{
    public class UpdateManagerService
    {
        private readonly EftApiService _eftApiService;

        public UpdateManagerService(EftApiService eftApiService)
        {
            _eftApiService = eftApiService;
        }

        public async Task<UpdateSet?> FindUpdatePathAsync(EftVersion currentVersion)
        {
            var (allUpdates, error) = await _eftApiService.GetGameUpdatesAsync();
            if (allUpdates == null || !allUpdates.Any())
            {
                return null;
            }
            
            // --- THIS IS THE CORRECTED LOGIC ---

            // 1. Safely find the latest version available on the server.
            EftVersion? latestVersion = null;
            foreach (var update in allUpdates)
            {
                if (EftVersion.TryParse(update.Version, out var parsedVersion))
                {
                    if (latestVersion == null || parsedVersion > latestVersion.Value)
                    {
                        latestVersion = parsedVersion;
                    }
                }
            }

            if (latestVersion == null)
            {
                // We couldn't parse any valid version numbers from the server's list.
                return null;
            }

            // 2. Build the path from the current version to the latest version.
            var updatesDict = allUpdates.ToDictionary(u => u.FromVersion, u => u);
            var path = new List<GameUpdate>();
            var versionTracer = currentVersion;

            while (versionTracer < latestVersion.Value)
            {
                if (updatesDict.TryGetValue(versionTracer.ToString(), out var nextPatch))
                {
                    path.Add(nextPatch);
                    // Safely parse the next version in the chain.
                    if (EftVersion.TryParse(nextPatch.Version, out var nextVersion))
                    {
                        versionTracer = nextVersion;
                    }
                    else
                    {
                        // The update chain is broken.
                        return null; 
                    }
                }
                else
                {
                    // Could not find a patch from the current version. The chain is broken.
                    return null; 
                }
            }

            return new UpdateSet { Patches = path, TargetVersion = latestVersion.Value };
        }
    }
}