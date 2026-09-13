using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using AnchorChain;

namespace SeaPowerMP
{
    // The only public type in this assembly: Anchor Chain scans exported types and inspects their
    // interfaces before our resolver is installed, so nothing else may expose types from sibling DLLs.
    [ACPlugin(PluginInfo.Guid, PluginInfo.Name, PluginInfo.Version)]
    public sealed class AnchorChainEntry : IAnchorChainMod
    {
        public void TriggerEntryPoint()
        {
            // Anchor Chain loads us with Assembly.LoadFile, which does not resolve sibling DLLs
            // (SeaPowerMP.Core, LiteNetLib). The resolver must be installed before any method
            // touching them is JIT-compiled.
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
                if (name == null)
                    return null;
                string path = Path.Combine(directory, name + ".dll");
                if (!File.Exists(path))
                    return null;

                return AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name)
                       ?? Assembly.LoadFile(path);
            };
        }
    }
}
