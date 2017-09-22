cls
$RootDirectoryPath = $PSScriptRoot
$MsBuildExe = 'C:\Program Files (x86)\Microsoft Visual Studio\Preview\Enterprise\MSBuild\15.0\Bin\MSBuild.exe'
$XUnitExe = "$RootDirectoryPath\packages\xunit.runner.console.2.2.0\tools\xunit.console.exe"

& $MsBuildExe "$RootDirectoryPath\test\NuGet.Core.Tests\NuGet.Common.Test\NuGet.Common.Test.csproj"

If ($? -eq $False)
{
    Exit;
}

cls

& $XUnitExe "$RootDirectoryPath\test\NuGet.Core.Tests\NuGet.Common.Test\bin\Debug\net46\win7-x64\NuGet.Common.Test.exe" -class NuGet.Common.Test.PathResolverTests