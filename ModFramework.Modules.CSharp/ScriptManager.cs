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
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ModFramework.Modules.CSharp
{
    public delegate bool FileFoundHandler(string filepath);

    public class Globals
    {
        public Action? Dispose;
    }

    class CSScript : IDisposable
    {
        public string? FilePath { get; set; }
        public string? FileName { get; set; }
        public string? Content { get; set; }

        public object? LoadResult { get; set; }
        public object? LoadError { get; set; }

        public ScriptManager Manager { get; set; }
        private Globals? Globals { get; set; }

        private Script<object>? _script;

        public CSScript(ScriptManager manager)
        {
            Manager = manager;
        }

        public void Unload()
        {
            try
            {
                Globals?.Dispose?.Invoke();
                Globals = null;
                _script = null;
            }
            catch(Exception ex)
            {
                Console.Error.WriteLine(ex);
            }
        }

        public void Dispose()
        {
            Unload();
            FilePath = null;
            FileName = null;
            Content = null;
            LoadResult = null;
            LoadError = null;
        }

        public void Load()
        {
            try
            {
                Unload();
                if (FilePath is null || !File.Exists(FilePath)) throw new FileNotFoundException("Failed to find script file", FilePath);

                Content = File.ReadAllText(FilePath);
                var dirName = Path.GetDirectoryName(FilePath);
                if (dirName is null) throw new DirectoryNotFoundException("Failed to find script directory:" + (dirName ?? "<null>"));

                _script = CSharpScript.Create(Content, Manager.ScriptOptions.WithFilePath(FilePath), globalsType: typeof(Globals));

                //foreach (var reff in _script.GetCompilation().References)
                //    Console.WriteLine($"{reff.Display} - {File.Exists(reff.Display)}");

                Globals = new Globals();
                var state = _script.RunAsync(Globals).Result;
            }
            catch (Exception ex)
            {
                LoadError = ex;
                Console.WriteLine("[CS] Load failed");
                Console.WriteLine(ex);
            }
        }
    }

    public class ScriptManager : IDisposable
    {
        public string ScriptFolder { get; set; }

        public static event FileFoundHandler? FileFound;

        private List<CSScript> _scripts { get; } = new List<CSScript>();
        private FileSystemWatcher? _watcher { get; set; }

        public ModFwModder? Modder { get; set; }

        public CSharpLoader Loader { get; set; }
        public CSharpLoader.MetaData MetaData { get; set; }
        public ScriptOptions ScriptOptions { get; set; }

        public ScriptManager(
            string scriptFolder,
            ModFwModder? modder
        )
        {
            ScriptFolder = scriptFolder;
            Modder = modder;

            Loader = new CSharpLoader();
            MetaData = Loader.CreateMetaData();

            ScriptOptions = ScriptOptions.Default
                .WithReferences((MetaData.MetadataReferences ?? Enumerable.Empty<MetadataReference>()).Union(
                    Loader.GetAllSystemReferences().Select(f => MetadataReference.CreateFromFile(f))
                 ))
                .WithEmitDebugInformation(true)
                .WithFileEncoding(Encoding.UTF8)
            ;
        }

        CSScript CreateScriptFromFile(string file)
        {
            Console.WriteLine($"[CS] Loading {file}");

            var script = new CSScript(this)
            {
                FilePath = file,
                FileName = Path.GetFileNameWithoutExtension(file),
            };

            _scripts.Add(script);

            script.Load();

            return script;
        }

        public void Initialise()
        {
            var scripts = Directory.GetFiles(ScriptFolder, "*.cs", SearchOption.TopDirectoryOnly);
            foreach (var file in scripts)
            {
                if (FileFound?.Invoke(file) == false)
                    continue; // event was cancelled, they do not wish to use this file. skip to the next.

                CreateScriptFromFile(file);
            }
        }

        public bool WatchForChanges()
        {
            try
            {
                _watcher = new FileSystemWatcher(ScriptFolder);
                _watcher.Created += _watcher_Created;
                _watcher.Changed += _watcher_Changed;
                _watcher.Deleted += _watcher_Deleted;
                _watcher.Renamed += _watcher_Renamed;
                _watcher.Error += _watcher_Error;
                _watcher.EnableRaisingEvents = true;

                return true;
            }
            catch (Exception ex)
            {
                var orig = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex);

                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("[CS] FILE WATCHERS ARE NOT RUNNING");
                Console.WriteLine("[CS] Try running: export MONO_MANAGED_WATCHER=dummy");
                Console.ForegroundColor = orig;
            }
            return false;
        }

        private void _watcher_Error(object sender, ErrorEventArgs e)
        {
            Console.WriteLine("[CS] Error");
            Console.WriteLine(e.GetException());
        }

        private void _watcher_Renamed(object sender, RenamedEventArgs e)
        {
            if (!Path.GetExtension(e.FullPath).Equals(".cs", StringComparison.CurrentCultureIgnoreCase)) return;
            Console.WriteLine("[CS] Renamed: " + e.FullPath);
            var src = Path.GetFileNameWithoutExtension(e.OldFullPath);
            var dst = Path.GetFileNameWithoutExtension(e.FullPath);

            foreach (var s in _scripts)
            {
                if (s.FileName?.Equals(src) == true)
                {
                    s.FileName = dst;
                    s.FilePath = e.FullPath;
                }
            }
        }

        private void _watcher_Deleted(object sender, FileSystemEventArgs e)
        {
            if (!Path.GetExtension(e.FullPath).Equals(".cs", StringComparison.CurrentCultureIgnoreCase)) return;

            var name = Path.GetFileNameWithoutExtension(e.FullPath);
            var matches = _scripts.Where(x => x.FileName == name).ToArray();
            Console.WriteLine($"[CS] Deleted: {e.FullPath} [m:{matches.Count()}]");
            foreach (var script in matches)
            {
                try
                {
                    _scripts.Remove(script);
                    script.Dispose();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[JS] Unload failed {ex}");
                }
            }
        }

        private void _watcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (!Path.GetExtension(e.FullPath).Equals(".cs", StringComparison.CurrentCultureIgnoreCase)) return;
            var name = Path.GetFileNameWithoutExtension(e.FullPath);
            var matches = _scripts.Where(x => x.FileName == name).ToArray();
            Console.WriteLine($"[CS] Changed: {e.FullPath} [m:{matches.Count()}]");
            foreach (var script in matches)
            {
                script.Load();
            }
        }

        private void _watcher_Created(object sender, FileSystemEventArgs e)
        {
            if (!Path.GetExtension(e.FullPath).Equals(".cs", StringComparison.CurrentCultureIgnoreCase)) return;
            Console.WriteLine("[CS] Created: " + e.FullPath);
            CreateScriptFromFile(e.FullPath);
        }

        public void Dispose()
        {
            _watcher?.Dispose();

            var cscripts = _scripts;
            if (cscripts is not null)
            {
                foreach (var script in cscripts)
                {
                    script.Dispose();
                }
                cscripts.Clear();
            }
        }

        public void Cli()
        {
            var exit = false;
            do
            {
                Console.WriteLine("[CS] TEST MENU. Press C to exit");
                exit = (Console.ReadKey(true).Key == ConsoleKey.C);
            } while (!exit);
        }
    }
}
