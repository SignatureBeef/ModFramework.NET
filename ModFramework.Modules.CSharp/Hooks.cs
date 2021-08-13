/*
Copyright (C) 2020 DeathCradle

This file is part of Open Terraria API v3 (OTAPI)

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program. If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.IO;
using System.Reflection;

namespace ModFramework.Modules.CSharp
{
    [MonoMod.MonoModIgnore]
    public static class Hooks
    {
        [Modification(ModType.Read, "Loading CSharp script interface")]
        public static void OnModding(ModFwModder modder)
        {
            new CSharpLoader()
                .SetModder(modder)
                .LoadModifications();
        }

        [Modification(ModType.Write, "Compiling CSharp modules based on produced binary")]
        public static void OnPatched()
        {
            new CSharpLoader()
                .SetAutoLoadAssemblies(false)
                .LoadModifications("modules-patched");
        }

        [Modification(ModType.Runtime, "Loading CSharp script interface")]
        public static void OnRunning(Assembly runtimeAssembly)
        {
            new CSharpLoader()
                .AddConstants(runtimeAssembly)
                .LoadModifications();

            Launch();
        }

        static ScriptManager ScriptManager { get; set; } // prevent GC and issues with file watching
        static void Launch(ModFwModder? modder = null)
        {
            var rootFolder = Path.Combine(Path.Combine(CSharpLoader.GlobalRootDirectory, "plugins", "scripts"));
            Directory.CreateDirectory(rootFolder);

            Console.WriteLine($"[CS] Loading CSharp scripts from ./{rootFolder}");

            ScriptManager = new ScriptManager(rootFolder, modder);
            ScriptManager.Initialise();
            ScriptManager.WatchForChanges();
        }
    }
}
