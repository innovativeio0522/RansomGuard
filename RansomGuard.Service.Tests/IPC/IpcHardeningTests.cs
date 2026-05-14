using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using FluentAssertions;
using RansomGuard.Service.Communication;
using RansomGuard.Service;
using Xunit;

namespace RansomGuard.Tests.IPC
{
    public class IpcHardeningTests
    {
        [Fact]
        public void PipeSecurity_ShouldNotGrantEveryoneAccess()
        {
            var pipeSecurity = NamedPipeServer.CreatePipeSecurity();
            var rules = pipeSecurity
                .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                .Cast<PipeAccessRule>()
                .ToList();

            var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
            var localSystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
            var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

            rules.Should().NotContain(rule => rule.IdentityReference == everyoneSid);
            rules.Should().Contain(rule => rule.IdentityReference == authenticatedUsersSid &&
                                           rule.AccessControlType == AccessControlType.Allow &&
                                           rule.PipeAccessRights.HasFlag(PipeAccessRights.ReadWrite));
            rules.Should().Contain(rule => rule.IdentityReference == localSystemSid &&
                                           rule.AccessControlType == AccessControlType.Allow &&
                                           rule.PipeAccessRights.HasFlag(PipeAccessRights.FullControl));
            rules.Should().Contain(rule => rule.IdentityReference == administratorsSid &&
                                           rule.AccessControlType == AccessControlType.Allow &&
                                           rule.PipeAccessRights.HasFlag(PipeAccessRights.FullControl));
        }

        [Fact]
        public void ProgramDataAcl_ShouldRemoveExplicitEveryoneAccess()
        {
            var testDir = Path.Combine(Path.GetTempPath(), "RG_AclTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDir);

            try
            {
                var everyoneSid = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
                var authenticatedUsersSid = new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null);
                var localSystemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
                var administratorsSid = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);

                var di = new DirectoryInfo(testDir);
                var security = di.GetAccessControl();
                security.AddAccessRule(new FileSystemAccessRule(
                    everyoneSid,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                di.SetAccessControl(security);

                Worker.ApplyProgramDataAcl(testDir);

                var rules = di.GetAccessControl()
                    .GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier))
                    .Cast<FileSystemAccessRule>()
                    .ToList();

                rules.Should().NotContain(rule => rule.IdentityReference == everyoneSid);
                rules.Should().Contain(rule => rule.IdentityReference == authenticatedUsersSid &&
                                               rule.AccessControlType == AccessControlType.Allow &&
                                               rule.FileSystemRights.HasFlag(FileSystemRights.Modify));
                rules.Should().Contain(rule => rule.IdentityReference == localSystemSid &&
                                               rule.AccessControlType == AccessControlType.Allow &&
                                               rule.FileSystemRights.HasFlag(FileSystemRights.FullControl));
                rules.Should().Contain(rule => rule.IdentityReference == administratorsSid &&
                                               rule.AccessControlType == AccessControlType.Allow &&
                                               rule.FileSystemRights.HasFlag(FileSystemRights.FullControl));
            }
            finally
            {
                if (Directory.Exists(testDir))
                    Directory.Delete(testDir, recursive: true);
            }
        }
    }
}
