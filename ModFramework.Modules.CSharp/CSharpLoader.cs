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
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace ModFramework.Modules.CSharp;

[MonoMod.MonoModIgnore]
public delegate bool AssemblyFoundHandler(string filepath);

[MonoMod.MonoModIgnore]
public class CSharpLoader
{
    const string ConsolePrefix = "CSharp";
    const string ModulePrefix = "CSharpScript_";

    public ModFwModder? Modder { get; set; }
    public ModContext ModContext { get; set; }

    public bool AutoLoadAssemblies { get; set; } = true;
    public MarkdownDocumentor? MarkdownDocumentor { get; set; }

    public bool ClearExistingModifications { get; set; } = true;

    public static AssemblyLoadContext AssemblyContextDefault { get; set; } = AssemblyLoadContext.Default;
    public AssemblyLoadContext AssemblyContext { get; set; } = AssemblyContextDefault;

    public static List<string> DefaultSearchPaths { get; set; } = new() { "bin" };
    public List<string> SearchPaths { get; set; } = DefaultSearchPaths;

    public IFrameworkResolver FrameworkResolver { get; set; } = new DefaultFrameworkResolver();

    //public static string GlobalRootDirectory { get; set; } = Path.Combine("csharp");

    public string BaseDirectory { get; }
    public string PluginsDirectory => Path.Combine(BaseDirectory, "plugins");
    public string GeneratedDirectory => Path.Combine(BaseDirectory, "generated");

    public const String DefaultBaseDirectory = "csharp";

    public CSharpLoader(ModContext context, string baseDirectory = DefaultBaseDirectory)
    {
        ModContext = context;

        BaseDirectory = Path.Combine(context.BaseDirectory, baseDirectory);
    }

    public CSharpLoader SetAutoLoadAssemblies(bool autoLoad)
    {
        AutoLoadAssemblies = autoLoad;
        return this;
    }

    public CSharpLoader SetClearExistingModifications(bool state)
    {
        ClearExistingModifications = state;
        return this;
    }

    public CSharpLoader SetContext(ModContext context)
    {
        ModContext = context;
        return this;
    }

    public CSharpLoader SetModder(ModFwModder modder)
    {
        Modder = modder;

        modder.OnReadMod += (m, module) =>
        {
            if (module.Assembly.Name.Name.StartsWith(ModulePrefix))
            {
                // remove the top level program class
                var tlc = module.GetType("<Program>$");
                if (tlc != null)
                {
                    module.Types.Remove(tlc);
                }
                Modder.RelinkAssembly(module);
            }
        };

        return this;
    }

    //public delegate bool ExternalRefFound(string filepath);
    //public event ExternalRefFound? OnExternalRefFound;

    IEnumerable<MetadataReference> LoadExternalRefs(string path)
    {
        var assemblyPath = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (String.IsNullOrWhiteSpace(assemblyPath))
            assemblyPath = AppContext.BaseDirectory;

        if (ModContext is not null)
        {
            foreach (var ref_file in ModContext.ReferenceFiles)
            {
                if (!ModContext.PluginLoader.CanAddFile(ref_file))
                    continue;

                var filename = Path.GetFileName(ref_file);

                var full_path = ResolveFile(ref_file);
                var sys_path = Path.Combine(assemblyPath, ref_file);

                if (File.Exists(full_path))
                    yield return MetadataReference.CreateFromFile(full_path);

                else if (File.Exists(sys_path))
                    yield return MetadataReference.CreateFromFile(sys_path);

                else
                {
                    IEnumerable<string> matches = Enumerable.Empty<string>();
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                    {
                        var x64 = Path.Combine(AppContext.BaseDirectory, "runtimes", "osx-x64");
                        if (Directory.Exists(x64))
                            matches = Directory.GetFiles(x64, "*" + filename, SearchOption.AllDirectories);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                    {
                        var x64 = Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64");
                        if (Directory.Exists(x64))
                            matches = Directory.GetFiles(x64, "*" + filename, SearchOption.AllDirectories);
                    }
                    else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        var x64 = Path.Combine(AppContext.BaseDirectory, "runtimes", "win-x64");
                        if (Directory.Exists(x64))
                            matches = Directory.GetFiles(x64, "*" + filename, SearchOption.AllDirectories);
                    }

                    if (matches.Any())
                    {
                        var match = matches.First();
                        if (File.Exists(match))
                            yield return MetadataReference.CreateFromFile(match);
                    }
                    else throw new Exception($"Unable to resolve external reference: {ref_file}");
                }
            }
        }
    }

    public class CreateContextOptions
    {
        public MetaData? Meta { get; set; }
        public string? AssemblyName { get; set; }
        public string? OutDir { get; set; }
        public OutputKind OutputKind { get; set; }
        public IEnumerable<CompilationFile>? CompilationFiles { get; set; }
        public IEnumerable<string>? Constants { get; set; }
        public IEnumerable<string>? OutAsmPath { get; set; }
        public IEnumerable<string>? OutPdbPath { get; set; }
    }

    public class CompilationContextArgs : EventArgs
    {
        public IEnumerable<string>? CoreLibAssemblies { get; set; }
        public CompilationContext? Context { get; set; }
    }
    public static event EventHandler<CompilationContextArgs>? OnCompilationContext;

    public static bool DefaultIncludeLocalSystemAssemblies { get; set; } = true;
    public bool IncludeLocalSystemAssemblies { get; set; } = DefaultIncludeLocalSystemAssemblies;

    private IEnumerable<MetadataReference>? systemRefs = null;
    public IEnumerable<MetadataReference> GetAllSystemReferences()
    {
        if (systemRefs is not null) return systemRefs;

        var fw = FrameworkResolver.FindFramework();

        var files = Directory.GetFiles(fw, "*.dll");
        return systemRefs = files.Select(f => MetadataReference.CreateFromFile(Path.GetFullPath(f)));
    }

    public string ResolveFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        var filename = Path.GetFileName(path);

        foreach (var searchPath in SearchPaths)
        {
            if (!Directory.Exists(searchPath))
            {
                System.Diagnostics.Debug.WriteLine($"Skipping search path as it doesnt exist: {searchPath}");
                continue;
            }

            var spmatches = Directory.GetFiles(searchPath, filename, SearchOption.AllDirectories);
            if (spmatches.Any())
            {
                var match = spmatches.First();
                if (File.Exists(match))
                {
                    return new FileInfo(match).FullName;
                }
            }
        }

        if (String.IsNullOrWhiteSpace(dir))
        {
            if (!File.Exists(path))
                path = Path.Combine(Environment.CurrentDirectory, filename);
            else if (!File.Exists(path))
                path = Path.Combine(Environment.CurrentDirectory, "bin", filename);
            else if (!File.Exists(path))
                path = Path.Combine(AppContext.BaseDirectory, filename);
        }
        else path = Path.Combine(dir, filename);

        return new FileInfo(path).FullName;
    }

    public MetadataReference? TryCreateRefFromFile(string path)
    {
        path = ResolveFile(path);
        if (File.Exists(path))
            return MetadataReference.CreateFromFile(path);
        return null;
    }

    CompilationContext CreateContext(CreateContextOptions options)
    {
        var assemblyPath = Path.GetDirectoryName(typeof(object).GetTypeInfo().Assembly.Location);

        MemoryStream dllStream = new();
        MemoryStream pdbStream = new();
        MemoryStream xmlStream = new();

        var assemblyName = ModulePrefix + options.AssemblyName;

        if (options.OutDir is null) throw new Exception($"{nameof(options.OutDir)} is null");
        var outAsmPath = Path.Combine(options.OutDir, $"{assemblyName}.dll");
        var outPdbPath = Path.Combine(options.OutDir, $"{assemblyName}.pdb");
        var outXmlPath = Path.Combine(options.OutDir, $"{assemblyName}.xml");

        List<MetadataReference> refs = new();

        // kept here just as a reminder should i come back to need these
        // the idea would be to add a [r|R]references.mfw.cs file
        // for modules to define extra imports
        //MetadataReference[] _ref =
        //    DependencyContext.Default.CompileLibraries
        //    .SelectMany(cl => cl.ResolveReferencePaths())
        //    .Select(asm => MetadataReference.CreateFromFile(asm))
        //    .ToArray();

        void TryAddRef(MetadataReference mref)
        {
            if (!refs.Any(x => x.Display == mref.Display))
            {
                refs.Add(mref);
            }
        }

        if (options.Meta?.MetadataReferences != null)
            foreach (var mref in options.Meta.MetadataReferences)
            {
                TryAddRef(mref);
            }

        var compile_options = new CSharpCompilationOptions(options.OutputKind)
                .WithOptimizationLevel(OptimizationLevel.Debug)
                .WithPlatform(Platform.AnyCpu)
                .WithNullableContextOptions(NullableContextOptions.Disable)
                .WithAllowUnsafe(true);

        var cf = options.CompilationFiles;
        if (cf is null) throw new Exception($"{nameof(options.CompilationFiles)} is null");
        IEnumerable<SyntaxTree> syntaxTrees = (options.CompilationFiles?
            .Where(x => x.SyntaxTree is not null)?
            .Select(x => x.SyntaxTree!)
        ) ?? Enumerable.Empty<SyntaxTree>();

        var compilation = CSharpCompilation
            .Create(assemblyName, syntaxTrees, options: compile_options, references: GetAllSystemReferences())
            .AddReferences(refs)
        ;

        EmitOptions emitOptions = new(
            debugInformationFormat: DebugInformationFormat.PortablePdb,
            pdbFilePath: outPdbPath
        );

        CompilationContextArgs args = new()
        {
            //CoreLibAssemblies = libs,
            Context = new()
            {
                Compilation = compilation,
                EmitOptions = emitOptions,
                CompilationOptions = compile_options,
                DllStream = dllStream,
                PdbStream = pdbStream,
                XmlStream = xmlStream,
                DllPath = outAsmPath,
                PdbPath = outPdbPath,
                XmlPath = outXmlPath,
                CompilationFiles = options.CompilationFiles,
                ModificationParams = new[]
                {
                    this,
                },
            }
        };

        OnCompilationContext?.Invoke(this, args);

        return args.Context;
    }

    public class CompilationFile
    {
        public string? File { get; set; }
        public SyntaxTree? SyntaxTree { get; set; }
        public EmbeddedText? EmbeddedText { get; set; }
    }

    void ProcessXmlSyntax(string filePath, string type, SyntaxTree encoded)
    {
        var root = encoded?.GetRoot();
        var syntax = root as CompilationUnitSyntax;
        if (syntax is not null)
        {
            foreach (var member in syntax.Members)
            {
                ProcessXmlMember(filePath, type, member);
            }
        }
    }

    public CSharpLoader SetMarkdownDocumentor(MarkdownDocumentor documentor)
    {
        MarkdownDocumentor = documentor;
        return this;
    }

    MarkdownDocumentor? GetMarkdownDocumentor()
    {
        if (MarkdownDocumentor is not null)
            return MarkdownDocumentor;

        return Modder?.MarkdownDocumentor;
    }

    void ProcessXmlComment(string filePath, string type, SyntaxTrivia trivia)
    {
        var xml_node = trivia.GetStructure();

        if (xml_node is DocumentationCommentTriviaSyntax dcts)
        {
            var xml = dcts.Content;

            foreach (var node in dcts.Content)
            {
                if (node is XmlElementSyntax xes)
                {
                    foreach (var item in xes.Content)
                    {
                        if (item is XmlTextSyntax xts)
                        {
                            var comments = String.Join(string.Empty, xts.TextTokens.Select(x => x.Text.Trim()));
                            var cleaned = String.Join(" ", comments.Trim().Split('\n').Select(x => x.Trim()));
                            var doc = GetMarkdownDocumentor();

                            if (doc != null && !doc.Find<BasicComment>(r => r.FilePath == filePath && r.Comments == cleaned).Any())
                                doc.Add(new BasicComment()
                                {
                                    Comments = String.Join(" ", comments.Trim().Split('\n').Select(x => x.Trim())),
                                    Type = type,
                                    FilePath = filePath,
                                });
                        }
                        else
                        {

                        }
                    }
                }
                else
                {

                }
            }
        }
        else
        {

        }
    }

    void ProcessXmlMember(string filePath, string type, MemberDeclarationSyntax member)
    {
        var trivias = member.GetLeadingTrivia();

        foreach (var trv in trivias)
        {
            if (trv.IsKind(SyntaxKind.MultiLineDocumentationCommentTrivia))
            {
                ProcessXmlComment(filePath, type, trv);
            }
            else if (trv.IsKind(SyntaxKind.SingleLineDocumentationCommentTrivia))
            {
                ProcessXmlComment(filePath, type, trv);
            }
            else
            {

            }
        }

        if (member is NamespaceDeclarationSyntax nds)
        {
            foreach (var child in nds.Members)
            {
                ProcessXmlMember(filePath, type, child);
            }
        }
        else
        {

        }
    }

    IEnumerable<CompilationFile> PrepareFiles(IEnumerable<string> files, IEnumerable<string> constants, string type)
    {
        var items = ParseFiles(files, constants);
        foreach (var item in items)
        {
            if (GetMarkdownDocumentor() is not null && item.File is not null && item.SyntaxTree is not null)
            {
                ProcessXmlSyntax(item.File, type, item.SyntaxTree);
            }
        }
        return items;
    }

    public static IEnumerable<CompilationFile> ParseFiles(IEnumerable<string> files, IEnumerable<string> constants)
    {
        foreach (var file in files)
        {
            var folder = Path.GetFileName(Path.GetDirectoryName(file));

            var encoding = System.Text.Encoding.UTF8;
            var parse_options = CSharpParseOptions.Default
                .WithKind(SourceCodeKind.Regular)
                .WithPreprocessorSymbols(constants.Select(s => s.Replace("#define ", "")))
                .WithDocumentationMode(DocumentationMode.Parse)
                .WithLanguageVersion(LanguageVersion.Preview); // allows toplevel functions

            var src = File.ReadAllText(file);
            var source = SourceText.From(src, encoding);
            var encoded = CSharpSyntaxTree.ParseText(source, parse_options, file);
            var embedded = EmbeddedText.FromSource(file, source);

            yield return new()
            {
                File = file,
                SyntaxTree = encoded,
                EmbeddedText = embedded,
            };
        }
    }

    public class CompilationContext : IDisposable
    {
        private bool disposedValue;

        public CSharpCompilation? Compilation { get; set; }
        public EmitOptions? EmitOptions { get; set; }
        public CSharpCompilationOptions? CompilationOptions { get; set; }
        public MemoryStream? DllStream { get; set; }
        public MemoryStream? PdbStream { get; set; }
        public MemoryStream? XmlStream { get; set; }
        public string? DllPath { get; set; }
        public string? XmlPath { get; set; }
        public string? PdbPath { get; set; }
        public IEnumerable<CompilationFile>? CompilationFiles { get; set; }
        public IEnumerable<object>? ModificationParams { get; set; }

        public EmitResult Compile()
        {
            if (Compilation is null) throw new Exception($"{nameof(Compilation)} is null");
            if (DllStream is null) throw new Exception($"{nameof(DllStream)} is null");

            return Compilation.Emit(
                  peStream: DllStream,
                  pdbStream: PdbStream,
                  xmlDocumentationStream: XmlStream,
                  embeddedTexts: CompilationFiles?.Where(x => x.EmbeddedText is not null)?.Select(x => x.EmbeddedText!),
                  options: EmitOptions
            );
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    // TODO: dispose managed state (managed objects)
                    Compilation = null;
                    EmitOptions = null;
                    //EmbeddedTexts = null;
                    XmlStream?.Dispose();
                    PdbStream?.Dispose();
                    DllStream?.Dispose();
                    DllPath = null;
                    XmlPath = null;
                    PdbPath = null;
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~CompilationContext()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }

    void ProcessCompilation(string errorName, CompilationContext ctx, EmitResult compilationResult)
    {
        if (ctx.DllStream is null) throw new Exception($"{nameof(ctx.DllStream)} is null");
        if (ctx.PdbStream is null) throw new Exception($"{nameof(ctx.PdbStream)} is null");
        if (ctx.XmlStream is null) throw new Exception($"{nameof(ctx.XmlStream)} is null");
        if (ctx.DllPath is null) throw new Exception($"{nameof(ctx.DllPath)} is null");
        if (ctx.PdbPath is null) throw new Exception($"{nameof(ctx.PdbPath)} is null");
        if (ctx.XmlPath is null) throw new Exception($"{nameof(ctx.XmlPath)} is null");

        if (compilationResult.Success)
        {
            ctx.DllStream.Seek(0, SeekOrigin.Begin);
            ctx.PdbStream.Seek(0, SeekOrigin.Begin);
            ctx.XmlStream.Seek(0, SeekOrigin.Begin);

            File.WriteAllBytes(ctx.DllPath, ctx.DllStream.ToArray());
            File.WriteAllBytes(ctx.PdbPath, ctx.PdbStream.ToArray());
            File.WriteAllBytes(ctx.XmlPath, ctx.XmlStream.ToArray());

            if (AutoLoadAssemblies)
            {
                //if (PluginLoader.AssemblyLoader is null) throw new Exception($"{nameof(PluginLoader.AssemblyLoader)} is null");
                //var asm = PluginLoader.AssemblyLoader.Load(ctx.DllStream, ctx.PdbStream);
                //PluginLoader.AddAssembly(asm);

                if (ModContext is null) throw new Exception($"{nameof(ModContext)} is null");
                var asm = ModContext.PluginLoader.AssemblyLoader.LoadFromStream(ctx.DllStream, ctx.PdbStream);
                ModContext.PluginLoader.AddAssembly(asm);

                if (Modder != null)
                    Modder.ReadMod(ctx.DllPath);
                else
                {
                    ModContext.Apply(ModType.Runtime, new[] { asm });
                }
                //else Modifier.Apply(ModType.Runtime, null, new[] { asm }, optionalParams: ctx.ModificationParams); // relay on the runtime hook
            }
        }
        else
        {
            System.Text.StringBuilder error = new();
            foreach (var diagnostic in compilationResult.Diagnostics.Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error))
            {
                error.AppendLine(diagnostic.ToString());
            }

            error.AppendLine("Refs:");
            if (ctx.Compilation?.References != null)
            {
                foreach (var dep in ctx.Compilation.References)
                    error.AppendLine(dep.Display);
            }

            throw new Exception($"{error}\nCompilation errors above for file: {errorName}");
        }
    }

    string? LoadScripts(MetaData meta, IEnumerable<string> files, OutputKind outputKind, string assemblyName, string type)
    {
        try
        {
            var compilationFiles = PrepareFiles(files, meta.Constants ?? Enumerable.Empty<string>(), type);

            using var ctx = CreateContext(new()
            {
                Meta = meta,
                AssemblyName = assemblyName,
                OutDir = meta.OutputDirectory,
                OutputKind = outputKind,
                CompilationFiles = compilationFiles,
                Constants = meta.Constants,
            });

            var compilationResult = ctx.Compile();
#if DEBUG
            if (!compilationResult.Success)
            {

            }
#endif

            ProcessCompilation(assemblyName, ctx, compilationResult);

            if (compilationResult.Success)
                return ctx.DllPath;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{ConsolePrefix}] Load error: {ex}");
            throw;
        }
        return null;
    }

    void LoadSingleScripts(MetaData meta, string folder, OutputKind outputKind, string type)
    {
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories);

        foreach (var file in files)
        {
            if (ModContext?.PluginLoader.CanAddFile(file) == false)
                continue; // event was cancelled, they do not wish to use this file. skip to the next.

            var assemblyName = Path.GetFileNameWithoutExtension(file);

            //var outAsmPath = Path.Combine(meta.OutputDirectory, $"{assemblyName}.dll");
            //if (File.Exists(outAsmPath))
            //    continue;

            Console.WriteLine($"[{ConsolePrefix}] Loading script: {file}");

            LoadScripts(meta, new[] { file }, outputKind, assemblyName, type);
        }
    }

    public string GetTargetAssemblyDirectory()
    {
        if (String.IsNullOrEmpty(ModContext?.TargetAssemblyName))
            throw new ArgumentException($"{nameof(ModContext.TargetAssemblyName)} must be configured in {nameof(ModContext)}");

        return ModContext.TargetAssemblyName.ToLower();
    }

    void LoadScripts(MetaData meta)
    {
        var toplevel = Path.Combine(PluginsDirectory, "mods"/*scripts are used by real scripts*/, GetTargetAssemblyDirectory());
        if (Directory.Exists(toplevel))
            LoadSingleScripts(meta, toplevel, OutputKind.ConsoleApplication, "toplevel");
    }

    void LoadPatches(MetaData meta)
    {
        var patches = Path.Combine(PluginsDirectory, "patches", GetTargetAssemblyDirectory());
        if (Directory.Exists(patches))
            LoadSingleScripts(meta, patches, OutputKind.DynamicallyLinkedLibrary, "patch");
    }

    public List<string> LoadModules(MetaData meta, string folder)
    {
        List<string> paths = new();
        var modules = Path.Combine(PluginsDirectory, folder, GetTargetAssemblyDirectory());
        if (Directory.Exists(modules))
        {
            var moduleNames = Directory.EnumerateDirectories(modules, "*", SearchOption.TopDirectoryOnly);

            foreach (var dir in moduleNames)
            {
                if (ModContext?.PluginLoader.CanAddFile(dir) == false)
                    continue; // event was cancelled, they do not wish to use this file. skip to the next.

                var files = Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories)
                    .Where(file => ModContext?.PluginLoader.CanAddFile(file) != false);
                if (files.Any())
                {
                    var moduleName = Path.GetFileName(dir);
                    Console.WriteLine($"[{ConsolePrefix}] Loading module: {moduleName}");
                    var path = LoadScripts(meta, files, OutputKind.DynamicallyLinkedLibrary, moduleName, "module");
                    if (File.Exists(path))
                        paths.Add(path!);
                }
                else
                {
                    // skipped - not needed for this target.
                }
            }
        }
        return paths;
    }

    public class MetaData
    {
        public IEnumerable<string>? Constants { get; set; }
        public IEnumerable<MetadataReference>? MetadataReferences { get; set; }
        public string? OutputDirectory { get; set; }
    }

    ////public static string GlobalRootDirectory { get; set; } = Path.Combine("csharp");

    //public string PluginsDirectory { get; set; } = Path.Combine(GlobalRootDirectory, "plugins");
    //public string GeneratedDirectory { get; set; } = Path.Combine(GlobalRootDirectory, "generated");

    public List<string> Constants { get; set; } = new();

    const string MetaDataKey = "Module.CSharp.Constants";

    public MetaData CreateMetaData()
    {
        const string constants_path = "AutoGenerated.cs";
        var constants = File.Exists(constants_path)
            ? File.ReadAllLines(constants_path) : Enumerable.Empty<string>(); // bring across the generated constants

        if (ModContext is not null)
        {
            constants = constants.Concat(ModContext.ReferenceConstants);
        }

        var refs = LoadExternalRefs(PluginsDirectory).ToList();

        var dir = Path.Combine(GeneratedDirectory, GetTargetAssemblyDirectory());

        if (ClearExistingModifications)
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }
        Directory.CreateDirectory(dir);

        return new()
        {
            MetadataReferences = refs,
            Constants = Constants.Concat(constants),
            OutputDirectory = dir,
        };
    }

    void PreserveConstants(MetaData meta)
    {
        if (Modder?.Module is not null)
        {
            Modder.AddMetadata(MetaDataKey, Newtonsoft.Json.JsonConvert.SerializeObject(meta.Constants));
        }
    }

    public CSharpLoader AddConstants(params Assembly?[] assemblies)
    {
        foreach (var assembly in assemblies.Where(a => a is not null))
        {
            var constants = ReadConstants(assembly!);
            if (constants.Any())
            {
                AddConstants(constants);
            }
        }
        return this;
    }

    public CSharpLoader AddConstants(params string[] constants) => AddConstants((IEnumerable<string>)constants);

    public CSharpLoader AddConstants(IEnumerable<string> constants)
    {
        Constants.AddRange(constants);
        return this;
    }

    public static IEnumerable<string> ReadConstants(Assembly assembly)
    {
        IEnumerable<string> constants = Enumerable.Empty<string>();

        var json = assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .SingleOrDefault(x => x.Key == MetaDataKey)
            ?.Value;

        if (!String.IsNullOrWhiteSpace(json))
        {
            constants = Newtonsoft.Json.JsonConvert.DeserializeObject<IEnumerable<string>>(json) ?? constants;
        }

        return constants;
    }

    [Flags]
    public enum EModification
    {
        None = 0,

        Script = 1,
        Patch = 2,
        Module = 4,

        All = Script | Patch | Module,
    }

    /// <summary>
    /// Discovers .cs modifications or top-level scripts, compiles and registers them with MonoMod or ModFramework accordingly
    /// </summary>
    public MetaData? LoadModifications(string? moduleFolder = null, EModification types = EModification.All)
    {
        if (Directory.Exists(PluginsDirectory))
        {
            AddConstants(Assembly.GetCallingAssembly(), Assembly.GetEntryAssembly());

            var meta = CreateMetaData();

            if ((types & EModification.Script) != 0)
                LoadScripts(meta);

            if ((types & EModification.Patch) != 0)
                LoadPatches(meta);

            if ((types & EModification.Module) != 0)
                LoadModules(meta, moduleFolder ?? "modules");

            PreserveConstants(meta);
            return meta;
        }
        return null;
    }
}
