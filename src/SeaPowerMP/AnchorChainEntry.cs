using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AnchorChain;

namespace SeaPowerMP
{
    [ACPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class AnchorChainEntry : IAnchorChainMod
    {
        public void TriggerEntryPoint()
        {
            // Anchor Chain loads us with Assembly.LoadFile, which does not resolve sibling DLLs
            // (SeaPowerMP.Core). The resolver must be installed before any method touching Core is JIT-compiled.
            InstallSiblingResolver();
            Boot();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void Boot() => Plugin.Boot();

        private static void InstallSiblingResolver()
        {
            string? directory = Path.GetDirectoryName(typeof(AnchorChainEntry).Assembly.Location);
            if (string.IsNullOrEmpty(directory))
                return;

            AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
            {
                string? name = new AssemblyName(args.Name).Name;
                if (name == null || !name.StartsWith("SeaPowerMP", StringComparison.Ordinal))
                    return null;

                Assembly? loaded = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == name);
                if (loaded != null)
                    return loaded;

                string path = Path.Combine(directory, name + ".dll");
                return File.Exists(path) ? Assembly.LoadFile(path) : null;
            };
        }
    }
}
