using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace FlappyReDovahLauncher
{
    public enum GraphicsPreset
    {
        Performance,
        Balanced,
        Ultra
    }

    /// <summary>
    /// Handles display resolution auto-tuning, graphics quality presets,
    /// and cleaning temporary MO2/Papyrus junk files.
    /// </summary>
    internal static class GameConfigManager
    {
        public static Size GetPrimaryDisplayResolution()
        {
            try
            {
                IntPtr hdc = NativeWinAPI.GetDC(IntPtr.Zero);
                if (hdc != IntPtr.Zero)
                {
                    try
                    {
                        int w = NativeWinAPI.GetDeviceCaps(hdc, NativeWinAPI.DESKTOPHORZRES);
                        int h = NativeWinAPI.GetDeviceCaps(hdc, NativeWinAPI.DESKTOPVERTRES);
                        if (w > 0 && h > 0)
                        {
                            return new Size(w, h);
                        }
                    }
                    finally
                    {
                        NativeWinAPI.ReleaseDC(IntPtr.Zero, hdc);
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"GetPrimaryDisplayResolution via GetDeviceCaps failed: {ex.Message}");
            }

            var bounds = Screen.PrimaryScreen.Bounds;
            return new Size(bounds.Width, bounds.Height);
        }

        public static void CleanLocalContentCatalog()
        {
            try
            {
                string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrEmpty(localAppData)) return;

                string target = Path.Combine(localAppData, "Skyrim Special Edition", "ContentCatalog.txt");
                if (File.Exists(target))
                {
                    File.Delete(target);
                    LauncherLog.Info($"CleanLocalContentCatalog: removed {target}");
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn($"CleanLocalContentCatalog failed: {ex.Message}");
            }
        }

        public static IEnumerable<string> GetProfileIniPaths(string iniFileName)
        {
            string root = GameCatalog.InstallRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) yield break;

            string profDir = Path.Combine(root, "profiles");
            if (!Directory.Exists(profDir)) yield break;

            foreach (var p in Directory.GetDirectories(profDir))
            {
                string target = Path.Combine(p, iniFileName);
                if (File.Exists(target)) yield return target;
            }
        }

        public static bool AutoConfigureResolution(out int width, out int height, out int profilesUpdated)
        {
            var res = GetPrimaryDisplayResolution();
            width = res.Width;
            height = res.Height;
            profilesUpdated = 0;

            foreach (string iniPath in GetProfileIniPaths("SkyrimPrefs.ini"))
            {
                try
                {
                    UpdateIniKeyValue(iniPath, "Display", "iSize W", width.ToString());
                    UpdateIniKeyValue(iniPath, "Display", "iSize H", height.ToString());
                    UpdateIniKeyValue(iniPath, "Display", "bBorderless", "1");
                    UpdateIniKeyValue(iniPath, "Display", "bFull Screen", "0");
                    profilesUpdated++;
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn($"Failed to update resolution in {iniPath}: {ex.Message}");
                }
            }

            LauncherLog.Info($"AutoConfigureResolution: set {width}x{height} in {profilesUpdated} profile(s)");
            return profilesUpdated > 0;
        }

        public static bool ApplyGraphicsPreset(GraphicsPreset preset, out int profilesUpdated)
        {
            profilesUpdated = 0;

            foreach (string iniPath in GetProfileIniPaths("SkyrimPrefs.ini"))
            {
                try
                {
                    switch (preset)
                    {
                        case GraphicsPreset.Performance:
                            UpdateIniKeyValue(iniPath, "Display", "iShadowMapResolution", "1024");
                            UpdateIniKeyValue(iniPath, "Display", "fShadowDistance", "4000.0000");
                            UpdateIniKeyValue(iniPath, "Display", "fInteriorShadowDistance", "2500.0000");
                            UpdateIniKeyValue(iniPath, "Display", "bTreesReceiveShadows", "0");
                            UpdateIniKeyValue(iniPath, "Grass", "fGrassStartFadeDistance", "3500.0000");
                            UpdateIniKeyValue(iniPath, "Grass", "fGrassMaxStartFadeDistance", "5000.0000");
                            break;

                        case GraphicsPreset.Balanced:
                            UpdateIniKeyValue(iniPath, "Display", "iShadowMapResolution", "2048");
                            UpdateIniKeyValue(iniPath, "Display", "fShadowDistance", "6000.0000");
                            UpdateIniKeyValue(iniPath, "Display", "fInteriorShadowDistance", "3000.0000");
                            UpdateIniKeyValue(iniPath, "Display", "bTreesReceiveShadows", "1");
                            UpdateIniKeyValue(iniPath, "Grass", "fGrassStartFadeDistance", "5000.0000");
                            UpdateIniKeyValue(iniPath, "Grass", "fGrassMaxStartFadeDistance", "7000.0000");
                            break;

                        case GraphicsPreset.Ultra:
                            UpdateIniKeyValue(iniPath, "Display", "iShadowMapResolution", "4096");
                            UpdateIniKeyValue(iniPath, "Display", "fShadowDistance", "8000.0000");
                            UpdateIniKeyValue(iniPath, "Display", "fInteriorShadowDistance", "3500.0000");
                            UpdateIniKeyValue(iniPath, "Display", "bTreesReceiveShadows", "1");
                            UpdateIniKeyValue(iniPath, "Grass", "fGrassStartFadeDistance", "7000.0000");
                            UpdateIniKeyValue(iniPath, "Grass", "fGrassMaxStartFadeDistance", "9000.0000");
                            break;
                    }
                    profilesUpdated++;
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn($"Failed to apply graphics preset in {iniPath}: {ex.Message}");
                }
            }

            LauncherLog.Info($"ApplyGraphicsPreset ({preset}): updated {profilesUpdated} profile(s)");
            return profilesUpdated > 0;
        }

        public static int CleanTemporaryFiles(out long freedBytes)
        {
            freedBytes = 0;
            int cleanedCount = 0;
            string root = GameCatalog.InstallRoot;

            // 1. Clean overwrite folder in InstallRoot (remove .log, .tmp, empty directories)
            if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
            {
                string overwrite = Path.Combine(root, "overwrite");
                if (Directory.Exists(overwrite))
                {
                    try
                    {
                        foreach (var f in Directory.GetFiles(overwrite, "*.*", SearchOption.AllDirectories))
                        {
                            string ext = Path.GetExtension(f).ToLowerInvariant();
                            if (ext == ".log" || ext == ".tmp" || ext == ".bak" || ext == ".dmp")
                            {
                                try
                                {
                                    var fi = new FileInfo(f);
                                    freedBytes += fi.Length;
                                    fi.Delete();
                                    cleanedCount++;
                                }
                                catch { }
                            }
                        }

                        // Remove empty subdirectories inside overwrite
                        foreach (var d in Directory.GetDirectories(overwrite, "*", SearchOption.AllDirectories))
                        {
                            try
                            {
                                if (Directory.Exists(d) && Directory.GetFileSystemEntries(d).Length == 0)
                                {
                                    Directory.Delete(d, false);
                                    cleanedCount++;
                                }
                            }
                            catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Warn("Clean overwrite failed: " + ex.Message);
                    }
                }
            }

            // 2. Clean Papyrus Logs in Documents
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string[] papyrusDirs = {
                    Path.Combine(docs, "My Games", "Skyrim Special Edition", "Logs", "Script"),
                    Path.Combine(docs, "My Games", "Skyrim VR", "Logs", "Script")
                };

                foreach (var pDir in papyrusDirs)
                {
                    if (Directory.Exists(pDir))
                    {
                        foreach (var f in Directory.GetFiles(pDir, "*.log", SearchOption.TopDirectoryOnly))
                        {
                            try
                            {
                                var fi = new FileInfo(f);
                                freedBytes += fi.Length;
                                fi.Delete();
                                cleanedCount++;
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("Clean papyrus logs failed: " + ex.Message);
            }

            LauncherLog.Info($"CleanTemporaryFiles: removed {cleanedCount} file(s), freed {PackageInstaller.FormatBytes(freedBytes)}");
            return cleanedCount;
        }

        private static void UpdateIniKeyValue(string filePath, string section, string key, string value)
        {
            if (!File.Exists(filePath)) return;

            var lines = new List<string>(File.ReadAllLines(filePath, Encoding.UTF8));
            bool inSection = false;
            bool foundKey = false;
            int sectionLineIndex = -1;

            string sectionHeader = "[" + section.Trim() + "]";

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i].Trim();
                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    if (string.Equals(line, sectionHeader, StringComparison.OrdinalIgnoreCase))
                    {
                        inSection = true;
                        sectionLineIndex = i;
                    }
                    else if (inSection)
                    {
                        // Exited section without finding key, insert before next section
                        lines.Insert(i, key + "=" + value);
                        foundKey = true;
                        break;
                    }
                }
                else if (inSection)
                {
                    int eqIndex = line.IndexOf('=');
                    if (eqIndex > 0)
                    {
                        string k = line.Substring(0, eqIndex).Trim();
                        if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
                        {
                            lines[i] = key + "=" + value;
                            foundKey = true;
                            break;
                        }
                    }
                }
            }

            if (!foundKey)
            {
                if (inSection)
                {
                    lines.Add(key + "=" + value);
                }
                else
                {
                    // Section not found, append section and key
                    lines.Add("");
                    lines.Add(sectionHeader);
                    lines.Add(key + "=" + value);
                }
            }

            File.WriteAllLines(filePath, lines, new UTF8Encoding(false));
        }
    }
}
