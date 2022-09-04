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

namespace ModFramework.Modules.CSharp;

[MonoMod.MonoModIgnore]
public static class Hooks
{
    public static ScriptManager? ScriptManager { get; set; } // prevent GC and issues with file watching
    public static CSharpLoader? Loader { get; set; }

    [Modification(ModType.Read, "Loading CSharp script interface")]
    public static void OnModding(ModFwModder modder)
    {
        Loader = new CSharpLoader(modder.ModContext)
            .SetModder(modder);

        Loader.LoadModifications();
    }

    [Modification(ModType.Write, "Compiling CSharp modules based on produced binary")]
    public static void OnPatched(ModContext modContext)
    {
        if (Loader is null)
        {
            Loader = new CSharpLoader(modContext)
                .SetAutoLoadAssemblies(false)
                .SetClearExistingModifications(false)
            ;
            Loader.LoadModifications("modules-patched");
        }
        else
        {
            Loader.SetContext(modContext)
                .SetAutoLoadAssemblies(false)
                .SetClearExistingModifications(false)
                .LoadModifications("modules-patched", CSharpLoader.EModification.Module);
        }

    }

    [Modification(ModType.Runtime, "Loading CSharp script interface")]
    public static void OnRunning(ModContext modContext, Assembly runtimeAssembly)
    {
        var loader = new CSharpLoader(modContext)
            .AddConstants(runtimeAssembly);

        loader.LoadModifications();

        Launch(loader, modContext);
    }

    static void Launch(CSharpLoader loader, ModContext modContext, ModFwModder? modder = null)
    {
        var rootFolder = Path.Combine(Path.Combine(loader.PluginsDirectory, "scripts"));
        Directory.CreateDirectory(rootFolder);

        Console.WriteLine($"[CS] Loading CSharp scripts from ./{new Uri(new DirectoryInfo(loader.PluginsDirectory).Parent.FullName).MakeRelativeUri(new(rootFolder))}");

        ScriptManager = new ScriptManager(rootFolder, modder, modContext, loader);
        ScriptManager.Initialise();
        ScriptManager.WatchForChanges();
    }

    [Modification(ModType.Shutdown, "Shutting down the CSharp script interface")]
    public static void OnShutdown()
    {
        ScriptManager?.Dispose();
        ScriptManager = null;
    }
}
