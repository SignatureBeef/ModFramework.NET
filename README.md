# ModFramework.NET  ![build](https://github.com/SignatureBeef/ModFramework.NET/actions/workflows/ci-build.yml/badge.svg) [![nuget (with prereleases)](https://img.shields.io/nuget/vpre/ModFramework)](https://nuget.org/packages/ModFramework) [![license: GPL v3](https://img.shields.io/badge/license-GPLv3-blue.svg)](https://www.gnu.org/licenses/gpl-3.0)
A tool for rewriting .net binaries. Can allow rewriting of Types to Interfaces, Arrays to Extensible Collections, or even Fields to Properties.

It is built on top of [Mono.Cecil](https://github.com/jbevain/cecil) & [Mono.Mod](https://github.com/MonoMod/MonoMod), it aims to allow mod developers to change application logic without having to touch [CIL](https://en.wikipedia.org/wiki/Common_Intermediate_Language) as much, yeilding much less technical code that most try and avoid.

### Examples
[Open-Terraria-API](https://github.com/SignatureBeef/Open-Terraria-API/tree/upcoming)
