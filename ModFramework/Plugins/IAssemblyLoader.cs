﻿/*
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
using System.Reflection;

namespace ModFramework.Plugins;

/// <summary>
/// Defines the bare minimum spec for ModFw to load assemblies
/// </summary>
/// <remarks>ModFw defines its own loaders because it used to support across multiple frameworks and AssemblyLoadContext is not always available</remarks>
public interface IAssemblyLoader
{
    Assembly Load(string path);
    Assembly Load(System.IO.MemoryStream assembly, System.IO.MemoryStream pdbsymbols);
}
