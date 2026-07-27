using System;
using System.Runtime.InteropServices;

namespace KiloVisualStudio
{
    /// <summary>
    /// GUIDs for the Kilo Code extension.
    /// </summary>
    internal static class PackageGuids
    {
        public const string KiloCodePackageString = "8a8f8e8c-1234-5678-9abc-def012345678";
        public const string KiloCodeContextString = "8a8f8e8c-1234-5678-9abc-def012345679";
        public const string KiloToolWindowString = "8a8f8e8c-1234-5678-9abc-def012345680";
        
        public static readonly Guid KiloCodePackage = new Guid(KiloCodePackageString);
        public static readonly Guid KiloCodeContext = new Guid(KiloCodeContextString);
        public static readonly Guid KiloToolWindow = new Guid(KiloToolWindowString);
    }

    /// <summary>
    /// Command IDs for the extension.
    /// </summary>
    internal static class CommandIds
    {
        public const int CmdNewTask = 0x0100;
        public const int CmdOpenSettings = 0x0101;
        public const int CmdOpenHistory = 0x0102;
    }
}
