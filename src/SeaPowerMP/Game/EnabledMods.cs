using System;
using System.Collections.Generic;
using System.IO;
using SeaPower;

namespace SeaPowerMP.Game
{
    internal static class EnabledMods
    {
        /// <summary>
        /// Display names of all mods ticked in the game's mod menu, excluding the base game folders
        /// and this mod itself (its version is compared separately in the handshake). Display names
        /// rather than folder names, because the same mod lives in "3769566124" when installed from
        /// the Workshop and in a named folder when installed locally.
        /// </summary>
        public static List<string> List(string ownModDirectory)
        {
            var mods = new List<string>();
            try
            {
                FileManager files = Singleton<FileManager>.Instance;
                string own = Path.GetFullPath(ownModDirectory).TrimEnd(Path.DirectorySeparatorChar);
                foreach (SearchDirectory directory in files.Directories)
                {
                    DirectoryInfo? info = directory?.DirectoryInfo;
                    if (info == null || !directory!.IsChecked)
                        continue;
                    if (files.specialDirectories != null && Array.IndexOf(files.specialDirectories, info) >= 0)
                        continue;
                    if (Path.GetFullPath(info.FullName).TrimEnd(Path.DirectorySeparatorChar).Equals(own, StringComparison.OrdinalIgnoreCase))
                        continue;
                    mods.Add(string.IsNullOrWhiteSpace(directory.Name) ? info.Name : directory.Name.Trim());
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning("Could not read the enabled mod list: " + ex.Message);
            }
            mods.Sort(StringComparer.OrdinalIgnoreCase);
            return mods;
        }
    }
}
