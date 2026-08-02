using System;
using System.Runtime.InteropServices;

namespace KiloVisualStudioExtension
{
    /// <summary>
    /// GUIDs for the Kilo Code extension package and components.
    /// These GUIDs are used for package identification, tool windows, and command sets.
    /// </summary>
    internal static class PackageGuids
    {
        /// <summary>
        /// Unique GUID for the Kilo Code package.
        /// </summary>
        public const string KiloCodePackageString = "8a8f8e8c-1234-5678-9abc-def012345678";
        
        /// <summary>
        /// GUID for the Kilo Code context.
        /// </summary>
        public const string KiloCodeContextString = "8a8f8e8c-1234-5678-9abc-def012345679";
        
        /// <summary>
        /// GUID for the Kilo tool window.
        /// </summary>
        public const string KiloToolWindowString = "8a8f8e8c-1234-5678-9abc-def012345680";
        
        /// <summary>
        /// GUID for the Kilo Code command set.
        /// </summary>
        public const string KiloCodeCmdSetString = "8a8f8e8c-1234-5678-9abc-def012345679";
        
        /// <summary>
        /// Guid object for the Kilo Code package.
        /// </summary>
        public static readonly Guid KiloCodePackage = new Guid(KiloCodePackageString);
        
        /// <summary>
        /// Guid object for the Kilo Code context.
        /// </summary>
        public static readonly Guid KiloCodeContext = new Guid(KiloCodeContextString);
        
        /// <summary>
        /// Guid object for the Kilo tool window.
        /// </summary>
        public static readonly Guid KiloToolWindow = new Guid(KiloToolWindowString);
        
        /// <summary>
        /// Guid object for the Kilo Code command set.
        /// </summary>
        public static readonly Guid KiloCodeCmdSet = new Guid(KiloCodeCmdSetString);
    }

    /// <summary>
    /// Command IDs for the extension toolbar and menu commands.
    /// These IDs are used in conjunction with the command set GUID to identify commands.
    /// </summary>
    internal static class CommandIds
    {
        /// <summary>
        /// Command ID for showing the Kilo Code tool window.
        /// </summary>
        public const int CmdShowWindow = 0x0100;
        
        /// <summary>
        /// Command ID for opening the Kilo Code settings panel.
        /// </summary>
        public const int CmdOpenSettings = 0x0101;
        
        /// <summary>
        /// Command ID for opening the history panel.
        /// </summary>
        public const int CmdOpenHistory = 0x0102;
    }
}
