using System;
using System.IO;
using BepInEx.Logging;
using SeaPower;

namespace SeaPowerMP.Game
{
    internal static class ModMenu
    {
        /// <summary>
        /// Anchor Chain loads every DLL in every mod folder, ticked or not, so the mod has to check
        /// the game's mod menu itself. Returns true when the state cannot be determined.
        /// </summary>
        public static bool IsEnabled(string assemblyLocation, ManualLogSource log)
        {
            try
            {
                string modDir = Path.GetFullPath(Path.GetDirectoryName(assemblyLocation) ?? "");
                if (modDir.Length == 0)
                    return true;

                foreach (SearchDirectory directory in Singleton<FileManager>.Instance.Directories)
                {
                    if (directory?.DirectoryInfo == null)
                        continue;
                    string root = Path.GetFullPath(directory.DirectoryInfo.FullName).TrimEnd(Path.DirectorySeparatorChar);
                    if (modDir.Equals(root, StringComparison.OrdinalIgnoreCase)
                        || modDir.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                        return directory.IsChecked;
                }
            }
            catch (Exception ex)
            {
                log.LogWarning("Could not read the mod menu state (" + ex.Message + "); loading anyway.");
            }
            return true;
        }
    }
}
