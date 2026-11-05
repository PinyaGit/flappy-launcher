using System;
using System.IO;
using System.Reflection;

namespace FlappyReDovahLauncher
{
    /// <summary>Creates Windows shell shortcuts (.lnk) via WScript.Shell late-binding.</summary>
    internal static class ShortcutHelper
    {
        public static bool CreateShortcut(string shortcutPath, string targetPath, string workingDir, string description = "")
        {
            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return false;

                object shell = Activator.CreateInstance(shellType);
                object shortcut = shellType.InvokeMember("CreateShortcut",
                    BindingFlags.InvokeMethod, null, shell, new object[] { shortcutPath });
                if (shortcut == null) return false;

                Type scType = shortcut.GetType();
                scType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath });
                scType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, shortcut, new object[] { workingDir });
                scType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, shortcut, new object[] { targetPath + ",0" });
                if (!string.IsNullOrEmpty(description))
                    scType.InvokeMember("Description", BindingFlags.SetProperty, null, shortcut, new object[] { description });

                scType.InvokeMember("Save", BindingFlags.InvokeMethod, null, shortcut, null);
                return true;
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Failed to create desktop shortcut: " + ex.Message);
                return false;
            }
        }
    }
}
