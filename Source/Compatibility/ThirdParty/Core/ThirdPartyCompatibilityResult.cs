using System;

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
            Exception? exception)
        {
            ModuleId = moduleId;
            DisplayName = displayName;
            PackageId = packageId;
            Status = status;
            Detail = detail;
            Exception = exception;
        }

        public string ModuleId { get; }

        public string DisplayName { get; }

        public string PackageId { get; }

        public ThirdPartyCompatibilityStatus Status { get; }

        public string Detail { get; }

        public Exception? Exception { get; }

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
                null);
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
                null);
        }

        public static ThirdPartyCompatibilityResult CreateTargetChanged(
            string moduleId,
            string displayName,
            string packageId,
            string detail)
        {
            return new ThirdPartyCompatibilityResult(
                moduleId,
                displayName,
                packageId,
                ThirdPartyCompatibilityStatus.TargetChanged,
                detail,
                null);
        }

        public static ThirdPartyCompatibilityResult CreateFailed(
            string moduleId,
            string displayName,
            string packageId,
            string detail,
            Exception? exception)
        {
            return new ThirdPartyCompatibilityResult(
                moduleId,
                displayName,
                packageId,
                ThirdPartyCompatibilityStatus.Failed,
                detail,
                exception);
        }
    }
}
