using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FlappyReDovahLauncher
{
    /// <summary>
    /// Manages backing up Skyrim save games from MO2 profiles and user documents.
    /// Used both manually via Settings -> Tools and automatically prior to applying delta updates.
    /// </summary>
    internal static class SaveBackupManager
    {
        public static string BackupsDirectory
        {
            get
            {
                string root = GameCatalog.InstallRoot;
                return Path.Combine(root, "Backups");
            }
        }

        public static string CreateBackup(out int savedFilesCount)
        {
            savedFilesCount = 0;
            string root = GameCatalog.InstallRoot;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return null;
            }

            var filesToBackup = new List<KeyValuePair<string, string>>(); // RelativePathInZip, FullDiskPath

            // 1. Scan MO2 profiles for saves: profiles\<profile>\saves\*
            string profilesDir = Path.Combine(root, "profiles");
            if (Directory.Exists(profilesDir))
            {
                try
                {
                    foreach (var profDir in Directory.GetDirectories(profilesDir))
                    {
                        string savesSub = Path.Combine(profDir, "saves");
                        if (Directory.Exists(savesSub))
                        {
                            string profName = Path.GetFileName(profDir);
                            foreach (var file in Directory.GetFiles(savesSub, "*.*", SearchOption.AllDirectories))
                            {
                                string ext = Path.GetExtension(file).ToLowerInvariant();
                                if (ext == ".ess" || ext == ".skse" || ext == ".bak" || ext == ".txt")
                                {
                                    string rel = "profiles/" + profName + "/saves/" + Path.GetFileName(file);
                                    filesToBackup.Add(new KeyValuePair<string, string>(rel, file));
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    LauncherLog.Warn("SaveBackupManager scan profiles: " + ex.Message);
                }
            }

            // 2. Scan standard documents saves (if present and modified recently or exist)
            try
            {
                string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                string[] docSaveDirs = new string[]
                {
                    Path.Combine(docs, "My Games", "Skyrim Special Edition", "Saves"),
                    Path.Combine(docs, "My Games", "Skyrim VR", "Saves")
                };

                foreach (var dsd in docSaveDirs)
                {
                    if (Directory.Exists(dsd))
                    {
                        string parentName = Path.GetFileName(Path.GetDirectoryName(dsd));
                        foreach (var file in Directory.GetFiles(dsd, "*.*", SearchOption.TopDirectoryOnly))
                        {
                            string ext = Path.GetExtension(file).ToLowerInvariant();
                            if (ext == ".ess" || ext == ".skse")
                            {
                                string rel = "Documents/" + parentName + "/Saves/" + Path.GetFileName(file);
                                filesToBackup.Add(new KeyValuePair<string, string>(rel, file));
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LauncherLog.Warn("SaveBackupManager scan documents: " + ex.Message);
            }

            if (filesToBackup.Count == 0)
            {
                return null;
            }

            string outDir = BackupsDirectory;
            Directory.CreateDirectory(outDir);

            string gameId = GameCatalog.Current != null ? GameCatalog.Current.Id : "game";
            string zipName = string.Format("Saves_{0}_{1:yyyyMMdd_HHmmss}.zip", gameId, DateTime.Now);
            string zipPath = Path.Combine(outDir, zipName);

            using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
            {
                foreach (var kvp in filesToBackup)
                {
                    try
                    {
                        archive.CreateEntryFromFile(kvp.Value, kvp.Key, CompressionLevel.Optimal);
                        savedFilesCount++;
                    }
                    catch (Exception ex)
                    {
                        LauncherLog.Warn("Failed to archive save file " + kvp.Value + ": " + ex.Message);
                    }
                }
            }

            LauncherLog.Info(string.Format("SaveBackupManager: created backup {0} with {1} file(s)", zipPath, savedFilesCount));
            return zipPath;
        }
    }
}
