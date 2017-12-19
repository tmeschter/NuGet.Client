using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace NuGet.Tests.Apex.NuGetIVsApiTests
{
    public class NetCoreProjectTestCase : SharedVisualStudioHostTestClass, IClassFixture<VisualStudioHostFixtureFactory>
    {
        public NetCoreProjectTestCase(VisualStudioHostFixtureFactory visualStudioHostFixtureFactory)
            : base(visualStudioHostFixtureFactory)
        {
        }

        [StaFact]
        public void CreateNetCoreProject_RestoresNewProject()
        {
            //using (var pathContext = new SimpleTestPathContext())
            //{
            //    // Arrange
            //    EnsureVisualStudioHost();
            //    var solutionService = VisualStudio.Get<SolutionService>();

            //    solutionService.CreateEmptySolution("TestSolution", pathContext.SolutionRoot);
            //    var project = solutionService.AddProject(ProjectLanguage.CSharp, ProjectTemplate.ClassLibrary, ProjectTargetFramework.V46, "TestProject");

            //    var packageName = "TestPackage";
            //    var packageVersion = "1.0.0";
            //    CreatePackageInSource(pathContext.PackageSource, packageName, packageVersion);

            //    var nugetTestService = GetNuGetTestService();
            //    Assert.True(nugetTestService.EnsurePackageManagerConsoleIsOpen());

            //    var nugetConsole = nugetTestService.GetPackageManagerConsole(project.Name);

            //    Assert.True(nugetConsole.InstallPackageFromPMC(packageName, packageVersion));
            //    Assert.True(nugetConsole.IsPackageInstalled(packageName, packageVersion));

            //    nugetConsole.Clear();
            //    solutionService.Save();
            //}
        }

    }
}
