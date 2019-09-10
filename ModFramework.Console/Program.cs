using System;

namespace ModFramework.Console
{
    class Program
    {
        static void Main(string[] args)
        {
            System.Console.WriteLine($"Current directory: {Environment.CurrentDirectory}");

            using (var fw = new Mod.Framework.ModFramework())
            {
                fw.RunModules();
            }
        }
    }
}
