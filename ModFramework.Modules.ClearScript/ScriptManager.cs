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
using Microsoft.ClearScript;
using Microsoft.ClearScript.JavaScript;
using Microsoft.ClearScript.V8;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
namespace ModFramework.Modules.ClearScript;

public delegate bool FileFoundHandler(string filepath);

public class JavascriptConsole
{
    public static void log(params object[] info)
    {
        for (var i = 0; i < info.Length; i++)
        {
            if (i > 0) Console.Write(", ");
            Console.Write(info[i]);
        }
        Console.WriteLine();
    }
}

class JSScript : IDisposable
{
    public string? FilePath { get; set; }
    public string? FileName { get; set; }
    public V8ScriptEngine? Container { get; set; }
    public V8Script? Script { get; set; }
    public string? Content { get; set; }
    public ModuleResolver? ModuleResolver { get; set; }

    public object? LoadResult { get; set; }
    public object? LoadError { get; set; }

    public ScriptManager Manager { get; set; }
    public bool IsModule { get; set; }

    public JSScript(ScriptManager manager, bool isModule)
    {
        Manager = manager;
        IsModule = isModule;
    }

    public void Unload()
    {
        Container?.Execute(new DocumentInfo { Category = ModuleCategory.Standard }, "import * as GlobalModule from 'GlobalModule';" +
            "if(typeof GlobalModule.Dispose == 'function') { GlobalModule.Dispose(); }");
        ModuleResolver?.Unload("GlobalModule");
    }

    public IEnumerable<string> GetComments()
    {
        if (Content is null) throw new ArgumentNullException(nameof(Content));
        return Content.Split('\n')
            .Where(line => (line.Trim().StartsWith("// @doc", StringComparison.CurrentCultureIgnoreCase)))
            .Select(v => v.Replace("// ", "").Trim());
    }

    void RegisterComments()
    {
        var doc = Manager.GetMarkdownDocumentor();
        if (doc is not null)
        {
            var comments = this.GetComments();
            if (comments.Any())
            {
                foreach (var comment in comments)
                {
                    doc.Add(new BasicComment()
                    {
                        Comments = comment,
                        Type = "script",
                        FilePath = this.FilePath,
                    });
                }
            }
        }
    }

    public void Dispose()
    {
        if (Script != null) Unload();
        Script?.Dispose();
        Container?.Dispose();
        ModuleResolver?.Dispose();
        ModuleResolver = null;
        Script = null;
        FilePath = null;
        FileName = null;
        Container = null;
        Content = null;
        LoadResult = null;
        LoadError = null;
    }

    public void Load()
    {
        try
        {
            if (Container != null) Unload();
        }
        catch (Exception ex)
        {
            Console.WriteLine("[JS] Unload failed");
            Console.WriteLine(ex);
        }
        try
        {
            Script?.Dispose();
            Script = null;
            Container?.Dispose();
            Container = null;
            ModuleResolver?.Dispose();
            ModuleResolver = null;

            if (FilePath is null || !File.Exists(FilePath)) throw new FileNotFoundException("Failed to find script file", FilePath);

            Content = File.ReadAllText(FilePath);
            Container = new(V8ScriptEngineFlags.EnableDynamicModuleImports);

            Container.DocumentSettings.AccessFlags = DocumentAccessFlags.EnableFileLoading;
            var dirName = Path.GetDirectoryName(FilePath);
            if (dirName is null) throw new DirectoryNotFoundException("Failed to find script directory:" + (dirName ?? "<null>"));
            Container.DocumentSettings.SearchPath = Path.GetFullPath(dirName);

            ModuleResolver = new(Container, Container.DocumentSettings.Loader);
            Container.DocumentSettings.Loader = ModuleResolver;

            Container.AddHostObject("host", new ExtendedHostFunctions());
            //Container.AddHostType(typeof(Console));
            Container.AddHostType("console", typeof(JavascriptConsole));
            Container.AllowReflection = true;

            if (Manager.Modder != null)
            {
                //Container.AddHostObject("Terraria", new HostTypeCollection("Terraria"));
                Container.AddHostObject("Modder", Manager.Modder);
            }
            //else
            //{
            //    Container.AddHostObject("OTAPI", new HostTypeCollection("OTAPI"));
            //    Container.AddHostObject("OTAPIRuntime", new HostTypeCollection("OTAPI.Runtime"));
            //}

            //Container.DocumentSettings.Loader.

            //Script = Container.Compile(new DocumentInfo { Category = ModuleCategory.Standard }, Content);
            //LoadResult = Container.Evaluate(Script);

            ModuleResolver.AddDocument("GlobalModule", Content, ModuleCategory.Standard);

            //LoadResult = Container.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, Content);
            LoadResult = Container.Evaluate(new DocumentInfo { Category = ModuleCategory.Standard }, "import * as GlobalModule from 'GlobalModule';");

            RegisterComments();
        }
        catch (Exception ex)
        {
            LoadError = ex;
            Console.WriteLine("[JS] Load failed");
            Console.WriteLine(ex);
        }
    }
}

public class ScriptManager : IDisposable
{
    public string ScriptFolder { get; set; }

    private List<JSScript> _scripts { get; } = new();
    private FileSystemWatcher? _watcher { get; set; }

    public ModFwModder? Modder { get; set; }
    public ModContext ModContext { get; set; }
    public MarkdownDocumentor? MarkdownDocumentor { get; set; }

    public ScriptManager(
        ModContext context,
        string scriptFolder,
        ModFwModder? modder
    )
    {
        ModContext = context;
        ScriptFolder = scriptFolder;
        Modder = modder;
    }

    public ScriptManager SetMarkdownDocumentor(MarkdownDocumentor documentor)
    {
        MarkdownDocumentor = documentor;
        return this;
    }

    public MarkdownDocumentor? GetMarkdownDocumentor()
    {
        if (MarkdownDocumentor is not null)
            return MarkdownDocumentor;

        return Modder?.MarkdownDocumentor;
    }

    JSScript CreateScriptFromFile(string file, bool module)
    {
        Console.WriteLine($"[JS] Loading {file}");

        JSScript script = new(this, module)
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
        var scripts = Directory.GetFiles(ScriptFolder, "*.js", SearchOption.TopDirectoryOnly);
        foreach (var file in scripts)
        {
            if (ModContext?.PluginLoader.CanAddFile(file) == false)
                continue; // event was cancelled, they do not wish to use this file. skip to the next.

            CreateScriptFromFile(file, false);
        }

        var modules = Directory.GetDirectories(ScriptFolder);
        foreach (var modulePath in modules)
        {
            if (Path.GetDirectoryName(modulePath)?.Equals("typings", StringComparison.CurrentCultureIgnoreCase) == true)
                continue;

            if (ModContext?.PluginLoader.CanAddFile(modulePath) == false)
                continue; // event was cancelled, they do not wish to use this file. skip to the next.

            var index = Path.Combine(modulePath, "index.js");

            if (!File.Exists(index)) // pure es6 file doesnt exist, try a potential typescript outpu
                index = Path.Combine(modulePath, "dist", "index.js");

            if (!File.Exists(index))
                throw new Exception($"[JS] index.js not found in module `{modulePath}`");

            CreateScriptFromFile(index, true);
        }
    }

    public bool WatchForChanges()
    {
        try
        {
            _watcher = new(ScriptFolder);
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
            Console.WriteLine("[JS] FILE WATCHERS ARE NOT RUNNING");
            Console.WriteLine("[JS] Try running: export MONO_MANAGED_WATCHER=dummy");
            Console.ForegroundColor = orig;
        }
        return false;
    }

    private void _watcher_Error(object sender, ErrorEventArgs e)
    {
        Console.WriteLine("[JS] Error");
        Console.WriteLine(e.GetException());
    }

    private void _watcher_Renamed(object sender, RenamedEventArgs e)
    {
        if (!Path.GetExtension(e.FullPath).Equals(".js", StringComparison.CurrentCultureIgnoreCase)) return;
        Console.WriteLine("[JS] Renamed: " + e.FullPath);
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
        if (!Path.GetExtension(e.FullPath).Equals(".js", StringComparison.CurrentCultureIgnoreCase)) return;

        var name = Path.GetFileNameWithoutExtension(e.FullPath);
        var matches = _scripts.Where(x => x.FileName == name).ToArray();
        Console.WriteLine($"[JS] Deleted: {e.FullPath} [m:{matches.Count()}]");
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
        if (!Path.GetExtension(e.FullPath).Equals(".js", StringComparison.CurrentCultureIgnoreCase)) return;
        var name = Path.GetFileNameWithoutExtension(e.FullPath);
        var matches = _scripts.Where(x => x.FileName == name).ToArray();
        Console.WriteLine($"[JS] Changed: {e.FullPath} [m:{matches.Count()}]");
        foreach (var script in matches)
        {
            script.Load();
        }
    }

    private void _watcher_Created(object sender, FileSystemEventArgs e)
    {
        if (!Path.GetExtension(e.FullPath).Equals(".js", StringComparison.CurrentCultureIgnoreCase)) return;
        Console.WriteLine("[JS] Created: " + e.FullPath);
        CreateScriptFromFile(e.FullPath, false);
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
            Console.WriteLine("[JS] TEST MENU. Press C to exit");
            exit = (Console.ReadKey(true).Key == ConsoleKey.C);
        } while (!exit);
    }
}
