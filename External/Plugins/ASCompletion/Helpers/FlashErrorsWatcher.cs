﻿using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using ASCompletion.Commands;
using PluginCore;
using PluginCore.Bridge;
using PluginCore.Helpers;
using PluginCore.Managers;
using Timer = System.Timers.Timer;

namespace ASCompletion.Helpers
{
    public class FlashErrorsWatcher
    {
        string logFile;
        string docInfo;
        string publishInfo;
        readonly WatcherEx fsWatcher;
        readonly Timer updater;

        readonly Regex reError = new Regex(
            @"^\*\*Error\*\*\s(?<file>.*\.as)[^0-9]+(?<line>[0-9]+)[:,\s]+(?<desc>[^\n\r]*)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        readonly Regex warnError = new Regex(
            @"^\*\*Warning\*\*\s(?<file>.*\.as)[^0-9]+(?<line>[0-9]+)[:,\s]+(?<desc>[^\n\r]*)",
            RegexOptions.Compiled | RegexOptions.Multiline);

        readonly Regex reFlashFile = new Regex(
            "<flashFileName>(?<output>[^<]+)</flashFileName>",
            RegexOptions.Compiled);

        public FlashErrorsWatcher()
        {
            try
            {
                string logLocation;
                if (BridgeManager.Active && BridgeManager.Settings.TargetRemoteIDE)
                {
                    logLocation = Path.Combine(BridgeManager.Settings.SharedDrive, ".FlashDevelop\\flashide");
                    Directory.CreateDirectory(logLocation);
                }
                else
                {
                    var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    logLocation = Path.Combine(appData, Path.Combine("Adobe", "FlashDevelop"));
                }

                Directory.CreateDirectory(logLocation);

                logFile = Path.Combine(logLocation, "FlashErrors.log");
                docInfo = Path.Combine(logLocation, "FlashDocument.log");
                publishInfo = Path.Combine(logLocation, "FlashPublish.log");

                fsWatcher = new WatcherEx(logLocation);
                fsWatcher.EnableRaisingEvents = true;
                fsWatcher.Changed += FsWatcher_Changed;

                updater = new Timer();
                updater.SynchronizingObject = PluginBase.MainForm as Form;
                updater.Interval = 200;
                updater.Elapsed += Updater_Tick;
            }
            catch { }
        }

        void Updater_Tick(object sender, EventArgs e)
        {
            updater.Stop();
            string src = File.Exists(logFile) ? File.ReadAllText(logFile) : "";
            MatchCollection errorMatches = reError.Matches(src);
            MatchCollection warningMatches = warnError.Matches(src);

            TextEvent te;
            if (errorMatches.Count == 0 && warningMatches.Count == 0)
            {
                te = new TextEvent(EventType.ProcessEnd, "Done(0)");
                EventManager.DispatchEvent(this, te);
                if (!te.Handled) PlaySWF();
                return;
            }

            NotifyEvent ne = new NotifyEvent(EventType.ProcessStart);
            EventManager.DispatchEvent(this, ne);
            foreach (Match m in errorMatches)
            {
                string file = m.Groups["file"].Value;
                string line = m.Groups["line"].Value;
                string desc = m.Groups["desc"].Value.Trim();
                Match mCol = Regex.Match(desc, @"\s*[a-z]+\s([0-9]+)\s");
                if (mCol.Success)
                {
                    line += "," + mCol.Groups[1].Value;
                    desc = desc.Substring(mCol.Length);
                }
                TraceManager.Add($"{file}({line}): {desc}", -3);
            }
            foreach (Match m in warningMatches)
            {
                string file = m.Groups["file"].Value;
                string line = m.Groups["line"].Value;
                string desc = m.Groups["desc"].Value.Trim();
                Match mCol = Regex.Match(desc, @"\s*[a-z]+\s([0-9]+)\s");
                if (mCol.Success)
                {
                    line += "," + mCol.Groups[1].Value;
                    desc = desc.Substring(mCol.Length);
                }
                TraceManager.Add($"{file}({line}): {desc}", -3);
            }
            te = new TextEvent(EventType.ProcessEnd, "Done(" + errorMatches.Count + ")");
            EventManager.DispatchEvent(this, te);

            if (errorMatches.Count == 0)
            {
                if (!te.Handled)
                {
                    PlaySWF();
                    return;
                }
            }
            
            ((Form) PluginBase.MainForm).Activate();
            ((Form) PluginBase.MainForm).Focus();
        }

        void PlaySWF()
        {
            if (!File.Exists(docInfo) || !File.Exists(publishInfo)) return;
            var fla = File.ReadAllText(docInfo);
            if (!File.Exists(fla)) return;

            try
            {
                // don't let another FD instance handle it
                // TODO Make multi-FD-instances friendly
                File.Delete(docInfo); 
            }
            catch { }

            var src = File.ReadAllText(publishInfo);
            var m = reFlashFile.Match(src);
            if (!m.Success) return;
            string output = m.Groups["output"].Value.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            output = PathHelper.ResolvePath(output, Path.GetDirectoryName(fla));
            if (!File.Exists(output)) return;
            FileInfo info = new FileInfo(output);
            CreateTrustFile.Run("FlashDevelop.cfg", info.Directory.FullName);
                        
            DataEvent de = new DataEvent(EventType.Command, "ProjectManager.PlayOutput", output);
            EventManager.DispatchEvent(this, de);
        }

        void FsWatcher_Changed(object sender, FileSystemEventArgs e)
        {
            if (fsWatcher.IsRemote)
            {
                var logLocation = Path.GetDirectoryName(e.FullPath);
                logFile = Path.Combine(logLocation, "FlashErrors.log");
                docInfo = Path.Combine(logLocation, "FlashDocument.log");
                publishInfo = Path.Combine(logLocation, "FlashPublish.log");
            }
            SetTimer();
        }

        void SetTimer()
        {
            updater.Enabled = false;
            updater.Enabled = true;
        }
    }
}
