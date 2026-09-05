using System;
using System.Collections.Generic;
using System.Reflection;

namespace MAP_MechanoidMechanitor.Compatibility.ThirdParty
{
    internal enum ThirdPartyCompatibilityStatus
    {
        Inactive,
        Applied,
        TargetChanged,
        Failed
    }

    internal sealed class ThirdPartyCompatibilityResult
    {
        private ThirdPartyCompatibilityResult(
            string moduleId,
            string displayName,
            string packageId,
            ThirdPartyCompatibilityStatus status,
            string detail,
            Exception? exception,
            MethodBase[] diagnosticTargets)
        {
            ModuleId = moduleId;
            DisplayName = displayName;
            PackageId = packageId;
            Status = status;
            Detail = detail;
            Exception = exception;
            DiagnosticTargets = diagnosticTargets;
        }

        public string ModuleId { get; }

        public string DisplayName { get; }

        public string PackageId { get; }

        public ThirdPartyCompatibilityStatus Status { get; }

        public string Detail { get; }

        public Exception? Exception { get; }

        public IReadOnlyList<MethodBase> DiagnosticTargets { get; }

        public static ThirdPartyCompatibilityResult CreateInactive(
            string moduleId,
            string displayName,
            string packageId)
        {
            return new ThirdPartyCompatibilityResult(
                moduleId,
                displayName,
                packageId,
                ThirdPartyCompatibilityStatus.Inactive,
                string.Empty,
                null,
                Array.Empty<MethodBase>());
        }

        public static ThirdPartyCompatibilityResult CreateApplied(
            string moduleId,
            string displayName,
            string packageId,
            string detail)
        {
            return new ThirdPartyCompatibilityResult(
                moduleId,
                displayName,
                packageId,
                ThirdPartyCompatibilityStatus.Applied,
                detail,
                null,
                Array.Empty<MethodBase>());
        }

        public static ThirdPartyCompatibilityResult CreateTargetChanged(
            string moduleId,
            string displayName,
            string packageId,
            string detail,
            params MethodBase[] diagnosticTargets)
        {
            return new ThirdPartyCompatibilityResult(
                moduleId,
                displayName,
                packageId,
                ThirdPartyCompatibilityStatus.TargetChanged,
                detail,
                null,
                diagnosticTargets ?? Array.Empty<MethodBase>());
        }

        public static ThirdPartyCompatibilityResult CreateFailed(
            string moduleId,
            string displayName,
            string packageId,
            string detail,
            Exception? exception,
            params MethodBase[] diagnosticTargets)
        {
            return new ThirdPartyCompatibilityResult(
                moduleId,
                displayName,
                packageId,
                ThirdPartyCompatibilityStatus.Failed,
                detail,
                exception,
                diagnosticTargets ?? Array.Empty<MethodBase>());
        }
    }
}
