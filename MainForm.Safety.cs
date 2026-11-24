using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MidiTool
{
    // Automatic backup of the list (restored at the next start) and handling of unexpected errors.
    public partial class MainForm
    {
        bool restoreList = true;   // reopen the last list when the program starts
        bool hideCredits = true;   // lyric windows leave out title / copyright / phone lines
        Action liveRefresh;        // asks an open live-lyrics window to redraw its text
        bool crashShown;           // only one error dialog at a time
        readonly Timer autosaveTimer = new Timer { Interval = 2000 };

        static string AppData { get { return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData); } }
        static string AutosavePath { get { return Path.Combine(AppData, "MidiTrimMerge.autosave.mtm"); } }
        static string LogPath { get { return Path.Combine(AppData, "MidiTrimMerge.log"); } }

        void InitSafety()
        {
            autosaveTimer.Tick += (s, e) => { autosaveTimer.Stop(); AutosaveNow(); };
        }

        // Called after every change; the list is written 2 seconds after the last one.
        void RequestAutosave()
        {
            if (!restoreList) return;
            autosaveTimer.Stop();
            autosaveTimer.Start();
        }

        void AutosaveNow()
        {
            try
            {
                autosaveTimer.Stop();
                if (!restoreList || clips.Count == 0)
                {
                    if (File.Exists(AutosavePath)) File.Delete(AutosavePath);
                    return;
                }
                string tmp = AutosavePath + ".tmp";
                File.WriteAllText(tmp, BuildPlaylistText(), new UTF8Encoding(false));   // write aside first: a crash mid-write must not destroy the old backup
                if (File.Exists(AutosavePath)) File.Delete(AutosavePath);
                File.Move(tmp, AutosavePath);
            }
            catch { }
        }

        // At start: bring back the list as it was when the program was closed (or crashed).
        void RestoreAutosave()
        {
            try
            {
                if (!restoreList || !File.Exists(AutosavePath)) return;
                var lines = File.ReadAllLines(AutosavePath, Encoding.UTF8);
                if (lines.Length == 0 || lines[0].Trim() != "MTM1") return;
                var missing = LoadPlaylistLines(lines);
                SetStatus(L.T("restored", clips.Count));
                if (missing.Count > 0)
                {
                    // the backup is rewritten with fewer songs soon: keep the full one aside
                    string keep = AutosavePath + ".bak";
                    try { File.Copy(AutosavePath, keep, true); } catch { }
                    MessageBox.Show(this, L.T("restoreMissing", string.Join("\n", missing), Path.GetFileName(keep)),
                        L.T("loadFail"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch { }
        }

        // An unexpected error somewhere: write it to a log, save the list, tell the user and offer to continue.
        public void HandleCrash(Exception ex, bool fatal)
        {
            try { File.AppendAllText(LogPath, DateTime.Now.ToString("s") + "\r\n" + ex + "\r\n\r\n"); } catch { }
            try { player.Stop(); } catch { }
            AutosaveNow();
            if (crashShown) return;
            crashShown = true;
            try
            {
                string text = L.T("crashText", ex.Message);
                if (fatal)
                {
                    MessageBox.Show(text + "\n\n" + L.T("crashFatal"), L.T("crashTitle"), MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                var r = MessageBox.Show(this, text + "\n\n" + L.T("crashAsk"), L.T("crashTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Error);
                if (r == DialogResult.No) Environment.Exit(1);
            }
            finally { crashShown = false; }
        }
    }
}
