using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace Mod.Framework.Tests
{
	internal class ClassToBeModified
	{
		public void Test()
		{
			Console.WriteLine("Native");
		}
	}

	[TestClass]
	public class FrameworkTests
	{
		[TestMethod]
		public void Modules()
		{
			using (var fw = new ModFramework())
			{
				fw.RunModules();
			}
		}

		[TestMethod]
		public void TypeQuery()
		{
			using (var fw = new ModFramework(typeof(FrameworkTests).Assembly))
			{
				var results = fw.Query("Mod.Framework.Tests.ClassToBeModified");
				Assert.AreEqual(results.Count, 1);
			}
		}

		[TestMethod]
		public void MethodQuery()
		{
			using (var fw = new ModFramework(typeof(FrameworkTests).Assembly))
			{
				var results = fw.Query("Mod.Framework.Tests.ClassToBeModified.Test()");
				Assert.AreEqual(results.Count, 1);
				Assert.IsInstanceOfType(results.Single().Instance, typeof(MethodDefinition));
			}
		}

		[TestMethod]
		public void Patching()
		{
			using (var fw = new ModFramework())
			{
				var test = fw.Query("Mod.Framework.Tests.ClassToBeModified.Test()").As<MethodDefinition>();
				var code = test.Decompile();

				test.ApplyPatch("");
			}
		}

		[TestMethod]
		public void Pattern()
		{
			using (var fw = new ModFramework())
			{
				var test = fw.Query("Mod.Framework.Tests.ClassToBeModified.Test()").As<MethodDefinition>();
				var pattern = test.FindPattern(OpCodes.Ldstr, OpCodes.Call);

				Assert.IsNotNull(pattern.first);
				Assert.IsNotNull(pattern.last);

				Assert.AreEqual(pattern.first.OpCode, OpCodes.Ldstr);
				Assert.AreEqual(pattern.last.OpCode, OpCodes.Call);
			}
		}
	}
}
