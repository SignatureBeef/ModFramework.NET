# ModFramework.NET  [![Build Status](https://travis-ci.org/DeathCradle/ModFramework.NET.svg?branch=netstandard-2.0)](https://travis-ci.org/DeathCradle/ModFramework.NET)
A framework you use to build mods or rewrite other .NET applications.

It is built on top of [Mono.Cecil](https://github.com/jbevain/cecil) and aims to allow mod developers to change application logic without having to touch [CIL](https://en.wikipedia.org/wiki/Common_Intermediate_Language) as much, yeilding much less technical code that most try and avoid.

### What can it do?
- Query types, fields, methods or instructions using a string and apply bulk actions
- Replace references. Handy for shims or patching between .net & .net core.
- Transform fields to properties
- Transform properties to be virtuals
- Prebuilt emitters to produce pre &/or post hooks, properties, interfaces, delegates
- Type & method cloning between assembilies (simple scenarios)
- Replace 2d class instance arrays with 2d interfaced arrays
- Replace 2d arrays with array providers using getter & setter indexers (WIP)
- Integrated Rosyln compiler to compile custom code and inject into target assemblies
- Transform assembly code access from private to public
- Remove calls using a simple stack counter

### Examples
[Open-Terraria-API](https://github.com/DeathCradle/Open-Terraria-API/tree/netstandard-2.0)
