using NuGet.Frameworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NuGet.PackageManagement
{
    internal static class NetStandardCompatibilityUtil
    {
        internal static bool DoesProjectTargetsNEt461ORGreater(NuGetFramework currentProjectFramework)
        {
            if (currentProjectFramework == null)
            {
                throw new ArgumentNullException(nameof(currentProjectFramework));
            }

            if (currentProjectFramework.IsDesktop()
                && currentProjectFramework.Version >= FrameworkConstants.CommonFrameworks.Net461.Version)
            {
                return true;
            }

            return false;
        }

        internal static bool IsNearestFrameworkNetStandard20OrGreater(NuGetFramework currentProjectFramework,
            IEnumerable<NuGetFramework> supportedFrameworks)
        {
            // we look at target frameworks supported by the package and determine if the nearest framework is netstandard2.0,
            // then we do need to install the compatibility package. We only do it once for the whole list of actions -
            // hence the !needsNetstandard20Assets condition.
            var frameworkReducer = new FrameworkReducer();
            var nearestFramework = frameworkReducer.GetNearest(currentProjectFramework, supportedFrameworks);
            if (nearestFramework != null
                && string.Equals(nearestFramework.Framework, FrameworkConstants.FrameworkIdentifiers.NetStandard, StringComparison.OrdinalIgnoreCase)
                    && nearestFramework.Version >= FrameworkConstants.CommonFrameworks.NetStandard20.Version)
            {
                return true;
            }

            return false;
        }

    }
}
