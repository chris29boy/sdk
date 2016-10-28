// Copyright (c) .NET Foundation and contributors. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.IO;
using Microsoft.NET.TestFramework;
using Microsoft.NET.TestFramework.Assertions;
using Microsoft.NET.TestFramework.Commands;
using Xunit;
using static Microsoft.NET.TestFramework.Commands.MSBuildTest;
using System.Linq;
using FluentAssertions;
using System.Xml.Linq;
using System.Runtime.Versioning;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using System;

namespace Microsoft.NET.Build.Tests
{
    public class GivenThatWeWantToBuildALibrary
    {
        private TestAssetsManager _testAssetsManager = TestAssetsManager.TestProjectsAssetsManager;

       [Fact]
        public void It_builds_the_library_successfully()
        {
            var testAsset = _testAssetsManager
                .CopyTestAsset("AppWithLibrary")
                .WithSource()
                .Restore(relativePath: "TestLibrary");

            var libraryProjectDirectory = Path.Combine(testAsset.TestRoot, "TestLibrary");

            var buildCommand = new BuildCommand(Stage0MSBuild, libraryProjectDirectory);
            buildCommand
                .Execute()
                .Should()
                .Pass();

            var outputDirectory = buildCommand.GetOutputDirectory("netstandard1.5");

            outputDirectory.Should().OnlyHaveFiles(new[] {
                "TestLibrary.dll",
                "TestLibrary.pdb",
                "TestLibrary.deps.json"
            });
        }

       [Fact]
        public void It_builds_the_library_twice_in_a_row()
        {
            var testAsset = _testAssetsManager
                .CopyTestAsset("AppWithLibrary")
                .WithSource()
                .Restore(relativePath: "TestLibrary");

            var libraryProjectDirectory = Path.Combine(testAsset.TestRoot, "TestLibrary");

            var buildCommand = new BuildCommand(Stage0MSBuild, libraryProjectDirectory);
            buildCommand
                .Execute()
                .Should()
                .Pass();

            buildCommand
                .Execute()
                .Should()
                .Pass();
        }

        [Fact]
        public void It_ignores_excluded_folders()
        {
            Action<GetValuesCommand> setup = getValuesCommand =>
            {
                foreach (string folder in new[] { "bin", "obj", "packages" })
                {
                    WriteFile(Path.Combine(getValuesCommand.ProjectRootPath, folder, "source.cs"),
                        "!InvalidCSharp!");
                }

                WriteFile(Path.Combine(getValuesCommand.ProjectRootPath, "Code", "Class1.cs"),
                    "public class Class1 {}");
            };

            var compileItems = GetItemsFromTestLibrary("Compile", setup);

            compileItems = compileItems.Where(i =>
                    !i.EndsWith("AssemblyAttributes.cs", System.StringComparison.OrdinalIgnoreCase) &&
                    !i.EndsWith("AssemblyInfo.cs"))
                .ToList();

            var expectedItems = new[]
            {
                "Helper.cs",
                @"Code\Class1.cs"
            }
            .Select(item => item.Replace('\\', Path.DirectorySeparatorChar))
            .ToArray();

            compileItems.Should().BeEquivalentTo(expectedItems);
        }

        [Fact]
        public void It_allows_excluded_folders_to_be_overridden()
        {
            Action<GetValuesCommand> setup = getValuesCommand =>
            {
                foreach (string folder in new[] { "bin", "obj", "packages" })
                {
                    WriteFile(Path.Combine(getValuesCommand.ProjectRootPath, folder, "source.cs"),
                        $"public class ClassFrom_{folder} {{}}");
                }

                WriteFile(Path.Combine(getValuesCommand.ProjectRootPath, "Code", "Class1.cs"),
                    "public class Class1 {}");
            };

            var compileItems = GetItemsFromTestLibrary("Compile", setup, "/p:DisableDefaultRemoves=true");

            compileItems = compileItems.Where(i =>
                    !i.EndsWith("AssemblyAttributes.cs", System.StringComparison.OrdinalIgnoreCase) &&
                    !i.EndsWith("AssemblyInfo.cs"))
                .ToList();

            var expectedItems = new[]
            {
                "Helper.cs",
                @"Code\Class1.cs",
                @"bin\source.cs",
                @"obj\source.cs",
                @"packages\source.cs"
            }
            .Select(item => item.Replace('\\', Path.DirectorySeparatorChar))
            .ToArray();


            compileItems.Should().BeEquivalentTo(expectedItems);
        }

        private List<string> GetItemsFromTestLibrary(string itemType, Action<GetValuesCommand> setup, params string[] msbuildArgs)
        {
            string targetFramework = "netstandard1.5";

            var testAsset = _testAssetsManager
                .CopyTestAsset("AppWithLibrary")
                .WithSource()
                .Restore(relativePath: "TestLibrary");

            var libraryProjectDirectory = Path.Combine(testAsset.TestRoot, "TestLibrary");

            var getValuesCommand = new GetValuesCommand(Stage0MSBuild, libraryProjectDirectory,
                targetFramework, itemType, GetValuesCommand.ValueType.Item);

            setup(getValuesCommand);

            getValuesCommand
                .Execute(msbuildArgs)
                .Should()
                .Pass();

            var itemValues = getValuesCommand.GetValues();

            return itemValues;
        }

        private void WriteFile(string path, string contents)
        {
            string folder = Path.GetDirectoryName(path);
            Directory.CreateDirectory(folder);
            File.WriteAllText(path, contents);
        }

        [Theory]
        [InlineData(".NETStandard,Version=v1.0", new[] { "NETSTANDARD1_0" }, false)]
        [InlineData("netstandard1.3", new[] { "NETSTANDARD1_3" }, false)]
        [InlineData("netstandard1.6", new[] { "NETSTANDARD1_6" }, false)]
        [InlineData("netstandard20", new[] { "NETSTANDARD2_0" }, false)]
        [InlineData("net45", new[] { "NET45" }, true)]
        [InlineData("net461", new[] { "NET461" }, true)]
        [InlineData("netcoreapp1.0", new[] { "NETCOREAPP1_0" }, false)]
        [InlineData(".NETPortable,Version=v4.5,Profile=Profile78", new string[] { }, false)]
        [InlineData(".NETFramework,Version=v4.0,Profile=Client", new string[] { "NET40" }, false)]
        [InlineData("Xamarin.iOS,Version=v1.0", new string[] { "XAMARINIOS1_0" }, false)]
        [InlineData("UnknownFramework,Version=v3.14", new string[] { "UNKNOWNFRAMEWORK3_14" }, false)]
        public void It_implicitly_defines_compilation_constants_for_the_target_framework(string targetFramework, string[] expectedDefines, bool buildOnlyOnWindows)
        {
            var testAsset = _testAssetsManager
                .CopyTestAsset("AppWithLibrary", "ImplicitFrameworkConstants", targetFramework)
                .WithSource();

            var libraryProjectDirectory = Path.Combine(testAsset.TestRoot, "TestLibrary");

            var getValuesCommand = new GetValuesCommand(Stage0MSBuild, libraryProjectDirectory,
                targetFramework, "DefineConstants");

            //  Update target framework in project
            var ns = XNamespace.Get("http://schemas.microsoft.com/developer/msbuild/2003");
            var project = XDocument.Load(getValuesCommand.FullPathProjectFile);

            var targetFrameworkProperties = project.Root
                .Elements(ns + "PropertyGroup")
                .Elements(ns + "TargetFramework")
                .ToList();

            targetFrameworkProperties.Count.Should().Be(1);

            if (targetFramework.Contains(",Version="))
            {
                //  We use the full TFM for frameworks we don't have built-in support for targeting, so we don't want to run the Compile target
                getValuesCommand.ShouldCompile = false;

                var frameworkName = new FrameworkName(targetFramework);

                var targetFrameworkProperty = targetFrameworkProperties.Single();
                targetFrameworkProperty.AddBeforeSelf(new XElement(ns + "TargetFrameworkIdentifier", frameworkName.Identifier));
                targetFrameworkProperty.AddBeforeSelf(new XElement(ns + "TargetFrameworkVersion", "v" + frameworkName.Version.ToString()));
                if (!string.IsNullOrEmpty(frameworkName.Profile))
                {
                    targetFrameworkProperty.AddBeforeSelf(new XElement(ns + "TargetFrameworkProfile", frameworkName.Profile));
                }

                //  For the NuGet restore task to work with package references, it needs the TargetFramework property to be set.
                //  Otherwise we would just remove the property.
                targetFrameworkProperty.SetValue(targetFramework);
            }
            else
            {
                getValuesCommand.ShouldCompile = true;
                targetFrameworkProperties.Single().SetValue(targetFramework);
            }

            if (buildOnlyOnWindows && !RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                getValuesCommand.ShouldCompile = false;
            }

            using (var file = File.CreateText(getValuesCommand.FullPathProjectFile))
            {
                project.Save(file);
            }

            testAsset.Restore(relativePath: "TestLibrary");

            getValuesCommand
                .Execute()
                .Should()
                .Pass();

            var definedConstants = getValuesCommand.GetValues();

            definedConstants.Should().BeEquivalentTo(new[] { "DEBUG", "TRACE" }.Concat(expectedDefines).ToArray());
        }
    }
}