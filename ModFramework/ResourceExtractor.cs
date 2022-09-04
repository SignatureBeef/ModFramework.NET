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
using Mono.Cecil;
using System;
using System.IO;
using System.Linq;

namespace ModFramework;

[MonoMod.MonoModIgnore]
public class ResourceExtractor
{
    /// <summary>
    /// Extracts embedded resources to a folder
    /// </summary>
    /// <param name="assembly">Assembly to find resources within</param>
    /// <param name="extractionFolder">Destination to save files</param>
    /// <param name="onOverrideSave">Determine if a embedded resource needs saving</param>
    public void Extract(AssemblyDefinition assembly, string extractionFolder, Func<Resource, bool>? onOverrideSave = null)
    {
        if (Directory.Exists(extractionFolder)) Directory.Delete(extractionFolder, true);
        Directory.CreateDirectory(extractionFolder);

        foreach (var module in assembly.Modules)
        {
            if (module.HasResources)
            {
                foreach (var resource in module.Resources.ToArray())
                {
                    if (resource.ResourceType == ResourceType.Embedded)
                    {
                        var er = resource as EmbeddedResource;
                        var data = er?.GetResourceData();

                        if (data is not null && data.Length > 2)
                        {
                            bool is_pe = data.Take(2).SequenceEqual(new byte[] { 77, 90 }); // MZ
                            if (is_pe || (onOverrideSave is not null && onOverrideSave(resource)))
                            {
                                MemoryStream ms = new(data);
                                var asm = AssemblyDefinition.ReadAssembly(ms);

                                File.WriteAllBytes(Path.Combine(extractionFolder, $"{asm.Name.Name}.dll"), data);
                                module.Resources.Remove(resource);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Extracts embedded resources to a folder
    /// </summary>
    /// <param name="inputFile">Path to the assembly</param>
    /// <param name="resourcesFolder">Destination to save files</param>
    /// <returns>Extraction folder</returns>
    /// <exception cref="FileNotFoundException"></exception>
    /// <exception cref="DirectoryNotFoundException"></exception>
    public string Extract(string inputFile, string? resourcesFolder = null)
    {
        if (string.IsNullOrEmpty(inputFile) || !File.Exists(inputFile)) throw new FileNotFoundException("Resource assembly was not found", inputFile);

        var input = resourcesFolder ?? Path.GetDirectoryName(inputFile);

        if (input is null) throw new DirectoryNotFoundException("Resource assembly parent directory was not found: " + (input ?? "<null>"));

        var extractionFolder = Path.Combine(input, "EmbeddedResources");
        using (MemoryStream asmms = new(File.ReadAllBytes(inputFile)))
        {
            var def = AssemblyDefinition.ReadAssembly(asmms);
            Extract(def, extractionFolder);
        }

        return extractionFolder;
    }
}
