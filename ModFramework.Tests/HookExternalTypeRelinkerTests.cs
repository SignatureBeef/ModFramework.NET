using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModFramework.Relinker;
using Mono.Cecil;
using System;
using System.Linq;

namespace ModFramework.Tests;

[TestClass]
public class HookExternalTypeRelinkerTests
{
    [AssemblyInitialize]
    public static void Initialize(TestContext testContext)
    {
        ModContext ctx = new("TEST");
        ModFwModder mm = new(ctx)
        {
            InputPath = typeof(HookExternalTypeRelinkerTests).Assembly.Location,
            OutputPath = "HookExternalTypeRelinkerTests.TestRelink.dll",
        };
        mm.AddTask<HookExternalTypeRelinker>(typeof(System.Console));
        mm.Read();
        mm.MapDependencies();
        mm.AutoPatch();
        mm.WriterParameters.SymbolWriterProvider = null;
        mm.WriterParameters.WriteSymbols = false;
        mm.Write();

        Assembly = AssemblyDefinition.ReadAssembly(mm.OutputPath);
    }

    static AssemblyDefinition? Assembly { get; set; }

    TypeDefinition? ExampleType => Assembly?.MainModule?.GetType("ModFramework.Tests.HookExternalTypeRelinkerTests/Example");

    void VerifyWriteLine(string methodName)
    {
        Assert.IsNotNull(Assembly);
        Assert.IsNotNull(ExampleType);

        var method = ExampleType.Methods.Single(x => x.Name == methodName);

        var call = method.Body.Instructions.Single(x => x.OpCode == Mono.Cecil.Cil.OpCodes.Call && 
            x.Operand is MethodReference mref && 
            mref.DeclaringType.FullName == HookEmitter.HookEventsNamespace + ".System.Console");
        var name = (call.Operand as MethodReference)?.Name;

        Assert.AreEqual(HookEmitter.HookMethodNamePrefix + "WriteLine", name);
    }

    [TestMethod]
    public void String() => VerifyWriteLine(nameof(Example.String));

    [TestMethod]
    public void Args() => VerifyWriteLine(nameof(Example.Args));

    [TestMethod]
    public void Parms() => VerifyWriteLine(nameof(Example.Parms));

    public static class Example
    {
        public static void String()
        {
            Console.WriteLine("Hello, World!");
        }

        public static void Args()
        {
            Console.WriteLine("Hello, World!", null, "test", 1);
        }

        public static void Parms()
        {
            Console.WriteLine("Hello, World!", null, "test", 1, 2, 3, 4);
        }
    }
}