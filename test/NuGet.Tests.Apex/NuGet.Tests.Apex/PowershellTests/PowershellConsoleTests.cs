// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Test.Apex.VisualStudio.Solution;
using NuGet.Test.Utility;
using Xunit;

namespace NuGet.Tests.Apex
{
    public class PowershellConsoleTests : SharedVisualStudioHostTestClass, IClassFixture<VisualStudioHostFixtureFactory>
    {
        public PowershellConsoleTests(VisualStudioHostFixtureFactory visualStudioHostFixtureFactory)
            : base(visualStudioHostFixtureFactory)
        {
        }

        [StaFact]
        public void PowershellConsole_VerifyProjectListWithSingleProject()
        {
            using (var pathContext = new SimpleTestPathContext())
            {
                // Arrange
                EnsureVisualStudioHost();
                var dte = VisualStudio.Dte;
                var solutionService = VisualStudio.Get<SolutionService>();
                var nugetTestService = GetNuGetTestService();

                solutionService.CreateEmptySolution("test", pathContext.SolutionRoot);

                solutionService.AddProject(ProjectLanguage.CSharp, ProjectTemplate.ClassLibrary, ProjectTargetFramework.V46, "TestProject");

                var project = dte.Solution.Projects.Item(1);

                nugetTestService.OpenPowershellConsole();

                // Act
                nugetTestService.ExecutePowershellCommand("install-package newtonsoft.json");

                // Assert
                nugetTestService.Verify.PackageIsInstalled(project.UniqueName, "newtonsoft.json");
            }
        }
    }
}
