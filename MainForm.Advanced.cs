using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MidiTool
{
    // Remembered settings, the "Advanced settings" window, auto-trim, karaoke text editor, playlists.
    public partial class MainForm
    {
        // ---------- remembered settings ----------
        bool remember = true;
        string lastOpenDir, lastSaveDir;

        static string SettingsPath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MidiTrimMerge.settings"); }
        }

        static Dictionary<string, string> ReadSettings()
        {
            var d = new Dictionary<string, string>();
            try
            {
                if (File.Exists(SettingsPath))
                    foreach (var line in File.ReadAllLines(SettingsPath, Encoding.UTF8))
                    {
                        int i = line.IndexOf('=');
                        if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                    }
            }
            catch { }
            return d;
        }

        void ApplySavedSettings()
        {
            var d = ReadSettings();
            string v;
            if (d.TryGetValue("remember", out v)) remember = v != "0";
            if (d.TryGetValue("restore", out v)) restoreList = v != "0";
            if (d.TryGetValue("hideCredits", out v)) hideCredits = v != "0";
            if (!remember) return;
            int n;
            decimal dec;
            if (d.TryGetValue("mode", out v) && int.TryParse(v, out n) && n >= 0 && n < cmbMode.Items.Count) cmbMode.SelectedIndex = n;
            if (d.TryGetValue("overlap", out v) && decimal.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out dec))
                overlap.Value = Math.Max(overlap.Minimum, Math.Min(overlap.Maximum, dec));
            if (d.TryGetValue("fade", out v)) chkFade.Checked = v != "0";
            if (d.TryGetValue("device", out v) && v.Length > 0)
            {
                int idx = MidiPlayer.GetDevices().IndexOf(v);
                if (idx >= 0) { outDevice = idx; outDeviceName = v; }
            }
            if (d.TryGetValue("openDir", out v)) lastOpenDir = v;
            if (d.TryGetValue("saveDir", out v)) lastSaveDir = v;

            int x, y, w, h;
            if (d.TryGetValue("x", out v) && int.TryParse(v, out x) && d.TryGetValue("y", out v) && int.TryParse(v, out y) &&
                d.TryGetValue("w", out v) && int.TryParse(v, out w) && d.TryGetValue("h", out v) && int.TryParse(v, out h) &&
                w >= MinimumSize.Width && h >= MinimumSize.Height)
            {
                var r = new Rectangle(x, y, w, h);
                if (Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r)))
                {
                    StartPosition = FormStartPosition.Manual;
                    Bounds = r;
                    if (d.TryGetValue("max", out v) && v == "1") WindowState = FormWindowState.Maximized;
                }
            }
        }

        void SaveSettings()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("remember=" + (remember ? "1" : "0"));
                sb.AppendLine("restore=" + (restoreList ? "1" : "0"));
                sb.AppendLine("hideCredits=" + (hideCredits ? "1" : "0"));
                if (remember)
                {
                    sb.AppendLine("mode=" + cmbMode.SelectedIndex);
                    sb.AppendLine("overlap=" + overlap.Value.ToString(CultureInfo.InvariantCulture));
                    sb.AppendLine("fade=" + (chkFade.Checked ? "1" : "0"));
                    if (outDeviceName != null) sb.AppendLine("device=" + outDeviceName);
                    if (lastOpenDir != null) sb.AppendLine("openDir=" + lastOpenDir);
                    if (lastSaveDir != null) sb.AppendLine("saveDir=" + lastSaveDir);
                    var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
                    sb.AppendLine("x=" + b.X); sb.AppendLine("y=" + b.Y); sb.AppendLine("w=" + b.Width); sb.AppendLine("h=" + b.Height);
                    sb.AppendLine("max=" + (WindowState == FormWindowState.Maximized ? "1" : "0"));
                }
                File.WriteAllText(SettingsPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
        }

        // ---------- advanced settings window ----------
        static readonly string[] GmNames =
        {
            "Acoustic Grand Piano", "Bright Acoustic Piano", "Electric Grand Piano", "Honky-tonk Piano", "Electric Piano 1", "Electric Piano 2", "Harpsichord", "Clavinet",
            "Celesta", "Glockenspiel", "Music Box", "Vibraphone", "Marimba", "Xylophone", "Tubular Bells", "Dulcimer",
            "Drawbar Organ", "Percussive Organ", "Rock Organ", "Church Organ", "Reed Organ", "Accordion", "Harmonica", "Tango Accordion",
            "Nylon Guitar", "Steel Guitar", "Jazz Guitar", "Clean Guitar", "Muted Guitar", "Overdriven Guitar", "Distortion Guitar", "Guitar Harmonics",
            "Acoustic Bass", "Fingered Bass", "Picked Bass", "Fretless Bass", "Slap Bass 1", "Slap Bass 2", "Synth Bass 1", "Synth Bass 2",
            "Violin", "Viola", "Cello", "Contrabass", "Tremolo Strings", "Pizzicato Strings", "Orchestral Harp", "Timpani",
            "String Ensemble 1", "String Ensemble 2", "Synth Strings 1", "Synth Strings 2", "Choir Aahs", "Voice Oohs", "Synth Voice", "Orchestra Hit",
            "Trumpet", "Trombone", "Tuba", "Muted Trumpet", "French Horn", "Brass Section", "Synth Brass 1", "Synth Brass 2",
            "Soprano Sax", "Alto Sax", "Tenor Sax", "Baritone Sax", "Oboe", "English Horn", "Bassoon", "Clarinet",
            "Piccolo", "Flute", "Recorder", "Pan Flute", "Blown Bottle", "Shakuhachi", "Whistle", "Ocarina",
            "Square Lead", "Sawtooth Lead", "Calliope Lead", "Chiff Lead", "Charang Lead", "Voice Lead", "Fifths Lead", "Bass + Lead",
            "New Age Pad", "Warm Pad", "Polysynth Pad", "Choir Pad", "Bowed Pad", "Metallic Pad", "Halo Pad", "Sweep Pad",
            "Rain FX", "Soundtrack FX", "Crystal FX", "Atmosphere FX", "Brightness FX", "Goblins FX", "Echoes FX", "Sci-Fi FX",
            "Sitar", "Banjo", "Shamisen", "Koto", "Kalimba", "Bag Pipe", "Fiddle", "Shanai",
            "Tinkle Bell", "Agogo", "Steel Drums", "Woodblock", "Taiko Drum", "Melodic Tom", "Synth Drum", "Reverse Cymbal",
            "Guitar Fret Noise", "Breath Noise", "Seashore", "Bird Tweet", "Telephone Ring", "Helicopter", "Applause", "Gunshot"
        };

        Form advForm;
        bool advSync;
        FlowLayoutPanel advFlow;
        readonly Label advSong = new Label();
        readonly Label lbTranspose = new Label();
        readonly Label lbSpeed = new Label();
        readonly Label lbVolume = new Label();
        readonly Label lbOwnSec = new Label();
        readonly Label lbChan = new Label();
        readonly Label lbInstr = new Label();
        readonly Label lbDevice = new Label();
        readonly NumericUpDown numTranspose = new NumericUpDown();
        readonly NumericUpDown numSpeed = new NumericUpDown();
        readonly NumericUpDown numVolume = new NumericUpDown();
        readonly NumericUpDown numOwn = new NumericUpDown();
        readonly ComboBox cmbChan = new ComboBox();
        readonly ComboBox cmbInstr = new ComboBox();
        readonly ComboBox cmbDevice = new ComboBox();
        readonly CheckBox chkOwn = new CheckBox();
        readonly CheckBox chkRemember = new CheckBox();
        readonly CheckBox chkRestore = new CheckBox();
        readonly GroupBox grpSong = new GroupBox();
        readonly GroupBox grpAll = new GroupBox();
        readonly GroupBox grpFile = new GroupBox();
        readonly Button btnResetSong = new Button();
        readonly Button btnAutoOne = new Button();
        readonly Button btnAutoAll = new Button();
        readonly Button btnMatchLoud = new Button();
        readonly Button btnLyrics = new Button();
        readonly Button btnLyricsPlain = new Button();
        readonly Button btnSavePl = new Button();
        readonly Button btnLoadPl = new Button();
        readonly Button advClose = new Button();
        readonly List<int> advChannels = new List<int>();   // channel numbers shown in cmbChan
        readonly List<Button> advButtons = new List<Button>();

        FlowLayoutPanel AdvRow(params Control[] controls)
        {
            var r = new FlowLayoutPanel
            {
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
                Margin = new Padding(0, 0, 0, 6)
            };
            r.Controls.AddRange(controls);
            return r;
        }

        // A titled box that grows with its content.
        void AdvGroup(GroupBox g, params Control[] rows)
        {
            var inner = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Location = new Point(10, (int)(20 * TextScale))
            };
            inner.Controls.AddRange(rows);
            g.AutoSize = true;
            g.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            g.Padding = new Padding(8, 4, 8, 8);
            g.Margin = new Padding(0, 0, 0, 10);
            g.Controls.Add(inner);
        }

        void ShowAdvanced()
        {
            if (advForm != null && !advForm.IsDisposed)
            {
                if (!advForm.Visible) { SyncAdvanced(); advForm.Show(this); }
                advForm.Activate();
                return;
            }

            advForm = new Form
            {
                Font = Font, Icon = Icon, ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false,
                StartPosition = FormStartPosition.CenterParent,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            // Closing would dispose the controls (they live in MainForm fields and are reused), so only hide the window.
            advForm.FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.UserClosing || e.CloseReason == CloseReason.None) { e.Cancel = true; advForm.Hide(); }
            };
            advFlow = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown, WrapContents = false,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(14, 12, 14, 8)
            };

            advSong.AutoSize = true;
            advSong.Margin = new Padding(0, 0, 0, 8);
            foreach (var l in new[] { lbTranspose, lbSpeed, lbVolume, lbChan, lbInstr, lbDevice })
            { l.AutoSize = false; l.TextAlign = ContentAlignment.MiddleLeft; l.Margin = new Padding(3, 0, 3, 0); }
            lbOwnSec.AutoSize = true; lbOwnSec.Margin = new Padding(3, 6, 3, 0);
            chkOwn.AutoSize = false; chkOwn.Margin = new Padding(3, 0, 3, 0); chkOwn.TextAlign = ContentAlignment.MiddleLeft;
            chkRemember.AutoSize = true;
            chkRemember.Margin = new Padding(3, 2, 0, 4);
            chkRestore.AutoSize = true;
            chkRestore.Margin = new Padding(3, 2, 0, 4);

            numTranspose.Minimum = -24; numTranspose.Maximum = 24; numTranspose.TextAlign = HorizontalAlignment.Right;
            numSpeed.Minimum = 25; numSpeed.Maximum = 200; numSpeed.Value = 100; numSpeed.TextAlign = HorizontalAlignment.Right;
            numVolume.Minimum = 10; numVolume.Maximum = 300; numVolume.Value = 100; numVolume.Increment = 5; numVolume.TextAlign = HorizontalAlignment.Right;
            numOwn.Minimum = 0; numOwn.Maximum = 60; numOwn.DecimalPlaces = 1; numOwn.Increment = 0.5m; numOwn.TextAlign = HorizontalAlignment.Right;
            foreach (var cb in new[] { cmbChan, cmbInstr, cmbDevice }) cb.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbInstr.MaxDropDownItems = 16;

            foreach (var b in new[] { btnResetSong, btnAutoOne, btnAutoAll, btnMatchLoud, btnLyrics, btnLyricsPlain, btnSavePl, btnLoadPl, advClose })
            {
                SetupButton(b);
                b.Margin = new Padding(3, 0, 6, 0);
                advButtons.Add(b);
            }

            numTranspose.ValueChanged += (s, e) => { var c = AdvClip(); if (c == null) return; PushUndo("transpose"); c.Transpose = (int)numTranspose.Value; SongChanged(c); };
            numSpeed.ValueChanged += (s, e) => { var c = AdvClip(); if (c == null) return; PushUndo("speed"); c.Speed = (int)numSpeed.Value; SongChanged(c); };
            numVolume.ValueChanged += (s, e) => { var c = AdvClip(); if (c == null) return; PushUndo("volume"); c.Volume = (int)numVolume.Value; SongChanged(c); };
            chkOwn.CheckedChanged += (s, e) =>
            {
                numOwn.Enabled = chkOwn.Checked && Selected != null;
                var c = AdvClip(); if (c == null) return;
                PushUndo("own");
                c.OverlapAfter = chkOwn.Checked ? (double)numOwn.Value : -1;
                SongChanged(c);
            };
            numOwn.ValueChanged += (s, e) => { var c = AdvClip(); if (c == null || !chkOwn.Checked) return; PushUndo("own"); c.OverlapAfter = (double)numOwn.Value; SongChanged(c); };
            cmbChan.SelectedIndexChanged += (s, e) => { if (!advSync) { advSync = true; ShowChannelInstrument(); advSync = false; } };
            cmbInstr.SelectedIndexChanged += (s, e) => InstrumentPicked();
            btnResetSong.Click += (s, e) =>
            {
                var c = Selected; if (c == null) return;
                PushUndo("reset");
                c.Transpose = 0; c.Speed = 100; c.Volume = 100; c.OverlapAfter = -1; c.Programs = null;
                SongChanged(c); SyncAdvanced();
            };
            btnAutoOne.Click += (s, e) => AutoTrimClicked(false);
            btnAutoAll.Click += (s, e) => AutoTrimClicked(true);
            btnMatchLoud.Click += (s, e) => MatchLoudness();
            btnLyrics.Click += (s, e) => ShowLyrics();
            btnLyricsPlain.Click += (s, e) => ShowPlainLyrics();
            btnSavePl.Click += (s, e) => SavePlaylist();
            btnLoadPl.Click += (s, e) => LoadPlaylist();
            cmbDevice.SelectedIndexChanged += (s, e) =>
            {
                if (advSync) return;
                outDevice = cmbDevice.SelectedIndex - 1;
                outDeviceName = outDevice >= 0 ? Convert.ToString(cmbDevice.SelectedItem) : null;
            };
            chkRemember.Checked = remember;
            chkRemember.CheckedChanged += (s, e) => remember = chkRemember.Checked;
            chkRestore.Checked = restoreList;
            chkRestore.CheckedChanged += (s, e) => { restoreList = chkRestore.Checked; if (restoreList) RequestAutosave(); else AutosaveNow(); };
            advClose.Click += (s, e) => advForm.Close();

            AdvGroup(grpSong,
                advSong,
                AdvRow(lbTranspose, numTranspose),
                AdvRow(lbSpeed, numSpeed),
                AdvRow(lbVolume, numVolume),
                AdvRow(chkOwn, numOwn, lbOwnSec),
                AdvRow(lbChan, cmbChan),
                AdvRow(lbInstr, cmbInstr),
                AdvRow(btnAutoOne, btnLyrics, btnLyricsPlain),
                AdvRow(btnResetSong));
            AdvGroup(grpAll, AdvRow(btnAutoAll, btnMatchLoud));
            AdvGroup(grpFile,
                AdvRow(btnSavePl, btnLoadPl),
                AdvRow(lbDevice, cmbDevice),
                chkRemember,
                chkRestore);
            advFlow.Controls.Add(grpSong);
            advFlow.Controls.Add(grpAll);
            advFlow.Controls.Add(grpFile);
            advFlow.Controls.Add(AdvRow(advClose));
            advForm.Controls.Add(advFlow);
            advForm.CancelButton = advClose;

            FillDevices();
            ScaleAdvanced();
            FillAdvanced();
            SyncAdvanced();
            // the three boxes get the same width (that of the widest one)
            advForm.PerformLayout();
            int gw = new[] { grpSong, grpAll, grpFile }.Max(g => g.PreferredSize.Width);
            foreach (var g in new[] { grpSong, grpAll, grpFile }) g.MinimumSize = new Size(gw, 0);
            advForm.Show(this);
        }

        void FillDevices()
        {
            advSync = true;
            cmbDevice.Items.Clear();
            cmbDevice.Items.Add(L.T("devDefault"));
            foreach (var d in MidiPlayer.GetDevices()) cmbDevice.Items.Add(d);
            cmbDevice.SelectedIndex = outDevice >= 0 && outDevice + 1 < cmbDevice.Items.Count ? outDevice + 1 : 0;
            advSync = false;
        }

        // Applies the current text size to the advanced window.
        void ScaleAdvanced()
        {
            if (advForm == null || advForm.IsDisposed) return;
            float f = TextScale;
            advForm.Font = Font;
            advSong.Font = new Font(Font, FontStyle.Bold);
            advSong.MaximumSize = new Size((int)(430 * f), 0);
            int lw = (int)(190 * f), rh = Math.Max((int)(26 * f), Font.Height + 8);
            foreach (var l in new[] { lbTranspose, lbSpeed, lbVolume, lbChan, lbInstr, lbDevice }) l.Size = new Size(lw, rh);
            chkOwn.Size = new Size(lw + (int)(110 * f), rh);
            numTranspose.Width = numSpeed.Width = numVolume.Width = numOwn.Width = (int)(75 * f);
            cmbChan.Width = cmbInstr.Width = (int)(250 * f);
            cmbDevice.Width = (int)(250 * f);
            foreach (var b in advButtons) b.MinimumSize = new Size((int)(90 * f), (int)(28 * f));
            chkRemember.MaximumSize = chkRestore.MaximumSize = new Size((int)(430 * f), 0);
        }

        void FillAdvanced()
        {
            if (advForm == null || advForm.IsDisposed) return;
            advForm.Text = L.T("advTitle");
            grpSong.Text = L.T("grpSong");
            grpAll.Text = L.T("grpAll");
            grpFile.Text = L.T("grpFile");
            lbTranspose.Text = L.T("transpose");
            lbSpeed.Text = L.T("speed");
            lbVolume.Text = L.T("volume");
            lbChan.Text = L.T("channel");
            lbInstr.Text = L.T("instrument");
            lbDevice.Text = L.T("device");
            chkOwn.Text = L.T("ovOwn");
            lbOwnSec.Text = L.T("overlapSec");
            btnResetSong.Text = L.T("resetSong");
            btnAutoOne.Text = L.T("autoOne");
            btnAutoAll.Text = L.T("autoAll");
            btnMatchLoud.Text = L.T("matchLoud");
            btnLyrics.Text = L.T("lyricsBtn");
            btnLyricsPlain.Text = L.T("lyricsPlain");
            btnSavePl.Text = L.T("savePl");
            btnLoadPl.Text = L.T("loadPl");
            chkRemember.Text = L.T("remember");
            chkRestore.Text = L.T("restoreOpt");
            advClose.Text = L.T("close");
            FillDevices();
            SyncAdvanced();
        }

        // The selected song, but only when the change did not come from SyncAdvanced itself.
        Clip AdvClip() { return advSync ? null : Selected; }

        // Shows the settings of the selected song in the advanced window.
        void SyncAdvanced()
        {
            if (advForm == null || advForm.IsDisposed) return;
            advSync = true;
            try
            {
                var c = Selected;
                bool has = c != null;
                advSong.Text = has ? L.T("advSong") + "\n" + c.Name : L.T("advNone");
                numTranspose.Enabled = numSpeed.Enabled = numVolume.Enabled = chkOwn.Enabled = btnResetSong.Enabled = btnAutoOne.Enabled =
                    btnLyrics.Enabled = btnLyricsPlain.Enabled = has;
                numTranspose.Value = has ? c.Transpose : 0;
                numSpeed.Value = has ? Math.Max(numSpeed.Minimum, Math.Min(numSpeed.Maximum, c.Speed)) : 100;
                numVolume.Value = has ? Math.Max(numVolume.Minimum, Math.Min(numVolume.Maximum, c.Volume)) : 100;
                bool own = has && c.OverlapAfter >= 0;
                chkOwn.Checked = own;
                numOwn.Value = own ? Math.Min(numOwn.Maximum, (decimal)Math.Round(c.OverlapAfter, 1)) : overlap.Value;
                numOwn.Enabled = own;
                btnAutoAll.Enabled = btnSavePl.Enabled = btnMatchLoud.Enabled = clips.Count > 0;
                FillChannels();
            }
            finally { advSync = false; }
        }

        // Fills the channel list (and the instrument box) for the selected song.
        void FillChannels()
        {
            var c = Selected;
            int keep = cmbChan.SelectedIndex;
            cmbChan.Items.Clear();
            advChannels.Clear();
            if (c != null)
                foreach (var kv in MidiOps.ProgramsUsed(c.Seq))
                {
                    int prog = kv.Value, now;
                    string name = GmNames[Math.Max(0, Math.Min(127, prog))];
                    if (c.Programs != null && c.Programs.TryGetValue(kv.Key, out now))
                        name += "  →  " + GmNames[Math.Max(0, Math.Min(127, now))];
                    cmbChan.Items.Add((kv.Key + 1) + ": " + name);
                    advChannels.Add(kv.Key);
                }
            if (cmbInstr.Items.Count == 0)
            {
                cmbInstr.Items.Add(L.T("instrOrig"));
                foreach (var n in GmNames) cmbInstr.Items.Add(n);
            }
            else cmbInstr.Items[0] = L.T("instrOrig");
            if (cmbChan.Items.Count > 0) cmbChan.SelectedIndex = keep >= 0 && keep < cmbChan.Items.Count ? keep : 0;
            cmbChan.Enabled = cmbInstr.Enabled = cmbChan.Items.Count > 0;
            ShowChannelInstrument();
        }

        // Puts the instrument chosen for the selected channel into the instrument box.
        void ShowChannelInstrument()
        {
            var c = Selected;
            int i = cmbChan.SelectedIndex, prog;
            if (c == null || i < 0 || i >= advChannels.Count) { cmbInstr.SelectedIndex = -1; return; }
            cmbInstr.SelectedIndex = c.Programs != null && c.Programs.TryGetValue(advChannels[i], out prog) ? prog + 1 : 0;
        }

        void InstrumentPicked()
        {
            var c = AdvClip();
            int i = cmbChan.SelectedIndex;
            if (c == null || i < 0 || i >= advChannels.Count || cmbInstr.SelectedIndex < 0) return;
            PushUndo("instrument");
            var d = c.Programs == null ? new Dictionary<int, int>() : new Dictionary<int, int>(c.Programs);   // never edit in place: undo keeps the old one
            int prog = cmbInstr.SelectedIndex - 1;
            if (prog < 0) d.Remove(advChannels[i]); else d[advChannels[i]] = prog;
            c.Programs = d.Count > 0 ? d : null;
            SongChanged(c);
            advSync = true;
            FillChannels();
            advSync = false;
        }

        // A song's settings changed: refresh its row and the total length.
        void SongChanged(Clip c)
        {
            int i = clips.IndexOf(c);
            if (i >= 0 && i < list.Items.Count)
            {
                var sub = list.Items[i].SubItems;
                sub[3].Text = UsedText(c);
                sub[5].Text = BpmText(c);
                sub[6].Text = KeyText(c);
            }
            UpdateTotal();
            UpdateSeekUi();
        }

        // Sets every song's volume so that they all sound about equally loud.
        void MatchLoudness()
        {
            if (clips.Count == 0) return;
            var loud = new double[clips.Count];
            for (int i = 0; i < clips.Count; i++)
            {
                try { loud[i] = MidiOps.MeasureLoudness(MidiOps.Trim(clips[i].Source, clips[i].Start, clips[i].End)); } catch { loud[i] = 0; }
            }
            var ok = loud.Where(x => x > 0).ToList();
            if (ok.Count == 0) return;
            double target = ok.Average();
            PushUndo("loudness");
            int changed = 0;
            for (int i = 0; i < clips.Count; i++)
            {
                if (loud[i] <= 0) continue;
                int v = (int)Math.Max(10, Math.Min(300, Math.Round(target / loud[i] * 100 / 5.0) * 5));   // steps of 5%
                if (v != clips[i].Volume) changed++;
                clips[i].Volume = v;
            }
            Refresh(SelIndex);
            SetStatus(L.T("loudDone", changed));
        }

        // ---------- auto-trim silence ----------
        static bool AutoTrim(Clip c)
        {
            double first, last;
            if (!MidiOps.FindNoteSpan(c.Seq, out first, out last)) return false;
            double s = Math.Max(0, first - 0.05);
            if (s < 0.1) s = 0;
            double e = last + 0.2;
            c.Start = s;
            c.End = e >= c.Duration - 0.005 ? 0 : e;
            return true;
        }

        void AutoTrimClicked(bool all)
        {
            int idx = list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0];
            int changed = 0;
            PushUndo("autotrim");
            if (all) { foreach (var c in clips) if (AutoTrim(c)) changed++; }
            else if (idx >= 0 && AutoTrim(clips[idx])) changed = 1;
            Refresh(idx);
            SetStatus(L.T("autoDone", changed));
        }

        // ---------- karaoke text ----------
        void ShowLyrics()
        {
            var c = Selected;
            if (c == null) return;
            var evs = MidiOps.LyricEvents(c.Seq);
            if (evs.Count == 0)
            {
                MessageBox.Show(advForm ?? (IWin32Window)this, L.T("noLyrics"), L.T("title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            float f = TextScale;
            var area = Screen.FromControl(this).WorkingArea;
            var tm = new TempoMap(c.Seq);
            int[] codes = { 0, 1250, 1251, 65001, 1252 };
            string[] encNames = { L.T("lyEncAuto"), "Windows-1250 (Latin: č ć š ž đ)", "Windows-1251 (Cyrillic: ћ ч ш ж ђ)", "UTF-8", "Windows-1252 (Western)" };
            Func<int, Encoding> encOf = code => code == 0 ? null : Encoding.GetEncoding(code);
            int cp = c.LyricEnc;

            using (var dlg = new Form
            {
                Text = L.T("lyricsTitle", c.Name),
                Font = Font, Icon = Icon, ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                MinimumSize = new Size(480, 340),
                ClientSize = new Size(Math.Min((int)(640 * f), area.Width - 40), Math.Min((int)(560 * f), area.Height - 100))
            })
            {
                // top: encoding + find/replace
                var lblEnc = new Label { Text = L.T("lyEnc"), AutoSize = true, Margin = new Padding(3, 6, 3, 0) };
                var cmbEnc = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = (int)(250 * f) };
                cmbEnc.Items.AddRange(encNames);
                cmbEnc.SelectedIndex = Math.Max(0, Array.IndexOf(codes, cp));
                var row1 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Padding = new Padding(8, 8, 8, 0) };
                row1.Controls.AddRange(new Control[] { lblEnc, cmbEnc });

                var lblFind = new Label { Text = L.T("lyFind"), AutoSize = true, Margin = new Padding(3, 6, 3, 0) };
                var txtFind = new TextBox { Width = (int)(110 * f) };
                var lblRepl = new Label { Text = L.T("lyRepl"), AutoSize = true, Margin = new Padding(10, 6, 3, 0) };
                var txtRepl = new TextBox { Width = (int)(110 * f) };
                var btnRepl = new Button { Text = L.T("lyReplAll"), AutoSize = true, MinimumSize = new Size((int)(80 * f), (int)(26 * f)) };
                var row2 = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Padding = new Padding(8, 4, 8, 0) };
                row2.Controls.AddRange(new Control[] { lblFind, txtFind, lblRepl, txtRepl, btnRepl });

                var hint = new Label { Text = L.T("lyHelp"), AutoSize = true, Dock = DockStyle.Top, Padding = new Padding(10, 8, 10, 4), MaximumSize = new Size((int)(620 * f), 0) };

                var grid = new DataGridView
                {
                    Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false, AllowUserToResizeRows = false,
                    RowHeadersVisible = false, BackgroundColor = SystemColors.Window, MultiSelect = false,
                    SelectionMode = DataGridViewSelectionMode.CellSelect, EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2,
                    AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCells
                };
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L.T("lyTime"), ReadOnly = true, Width = (int)(80 * f), SortMode = DataGridViewColumnSortMode.NotSortable });
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = L.T("lyText"), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
                for (int i = 0; i < evs.Count; i++) grid.Rows.Add(Fmt(tm.TickToSec(evs[i].Tick)), "");

                // fills the text column from the original bytes (or from earlier edits)
                Action<bool> load = useEdits =>
                {
                    var enc = encOf(codes[cmbEnc.SelectedIndex]);
                    for (int i = 0; i < evs.Count; i++)
                    {
                        string t = useEdits && c.LyricTexts != null && i < c.LyricTexts.Length ? c.LyricTexts[i] : null;
                        grid.Rows[i].Cells[1].Value = t ?? MidiOps.DecodeText(evs[i].Data, enc);
                    }
                };
                load(true);

                int lastEnc = cmbEnc.SelectedIndex;
                cmbEnc.SelectedIndexChanged += (s, e) =>
                {
                    if (cmbEnc.SelectedIndex == lastEnc) return;
                    if (MessageBox.Show(dlg, L.T("lyEncWarn"), L.T("title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    { cmbEnc.SelectedIndex = lastEnc; return; }
                    lastEnc = cmbEnc.SelectedIndex;
                    load(false);
                };

                btnRepl.Click += (s, e) =>
                {
                    if (txtFind.Text.Length == 0) return;
                    grid.EndEdit();
                    int n = 0;
                    foreach (DataGridViewRow r in grid.Rows)
                    {
                        string v = Convert.ToString(r.Cells[1].Value) ?? "";
                        if (v.Contains(txtFind.Text)) { r.Cells[1].Value = v.Replace(txtFind.Text, txtRepl.Text); n++; }
                    }
                    SetStatus(L.T("lyReplDone", n));
                };

                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8) };
                Func<string, Button> mk = key =>
                {
                    var b = new Button { Text = L.T(key), AutoSize = true, MinimumSize = new Size((int)(90 * f), (int)(28 * f)), Margin = new Padding(4) };
                    bar.Controls.Add(b);
                    return b;
                };
                var bCancel = mk("lyCancel");
                var bApply = mk("lyApply");
                var bReset = mk("lyReset");

                bCancel.Click += (s, e) => dlg.Close();
                bReset.Click += (s, e) =>
                {
                    PushUndo("lyrics");
                    c.Edited = null; c.LyricTexts = null; c.LyricEnc = 0;
                    cmbEnc.SelectedIndex = 0; lastEnc = 0;
                    load(false);
                    RefreshKaraokeCell(c);
                    UpdateTotal();
                };
                bApply.Click += (s, e) =>
                {
                    grid.EndEdit();
                    int code = codes[cmbEnc.SelectedIndex];
                    var enc = encOf(code);
                    var texts = new string[evs.Count];
                    bool any = false;
                    for (int i = 0; i < evs.Count; i++)
                    {
                        string cell = Convert.ToString(grid.Rows[i].Cells[1].Value) ?? "";
                        // automatic mode: only rewrite rows that were really changed, keep the rest byte for byte
                        if (code == 0 && cell == MidiOps.DecodeText(evs[i].Data, null)) continue;
                        texts[i] = cell;
                        any = true;
                    }
                    if (code != 0 && !any) any = true;
                    PushUndo("lyrics");
                    c.LyricEnc = code;
                    c.LyricTexts = any ? texts : null;
                    c.Edited = any ? MidiOps.WithLyrics(c.Seq, texts, enc) : null;
                    RefreshKaraokeCell(c);
                    UpdateTotal();
                    SetStatus(L.T("lyApplied", c.Name));
                    dlg.Close();
                };
                dlg.AcceptButton = null;
                dlg.CancelButton = bCancel;

                dlg.Controls.Add(grid);
                dlg.Controls.Add(bar);
                dlg.Controls.Add(hint);
                dlg.Controls.Add(row2);
                dlg.Controls.Add(row1);
                dlg.ShowDialog(this);
            }
        }

        // The words as ordinary running text (with any edits applied), read-only, with a Copy button.
        void ShowPlainLyrics()
        {
            var c = Selected;
            if (c == null) return;
            var enc = c.LyricEnc == 0 ? null : Encoding.GetEncoding(c.LyricEnc);
            Func<string> shownText = () =>
            {
                string t = MidiOps.ExtractLyrics(c.Source, enc, hideCredits);
                return t.Length == 0 && hideCredits ? L.T("onlyCredits") : t;
            };
            string text = shownText();
            if (MidiOps.ExtractLyrics(c.Source, enc, false).Length == 0)
            {
                MessageBox.Show(advForm ?? (IWin32Window)this, L.T("noLyrics"), L.T("title"), MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            float f = TextScale;
            var area = Screen.FromControl(this).WorkingArea;
            using (var dlg = new Form
            {
                Text = L.T("plainTitle", c.Name),
                Font = Font, Icon = Icon, ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent,
                MinimumSize = new Size(300, 250),
                ClientSize = new Size(Math.Min((int)(520 * f), area.Width - 40), Math.Min((int)(500 * f), area.Height - 100))
            })
            {
                var box = new TextBox
                {
                    Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill,
                    BackColor = SystemColors.Window, Font = new Font(Font.FontFamily, Font.Size * 1.25f), Text = text, TabStop = false
                };
                var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(8) };
                var close = new Button { Text = L.T("close"), AutoSize = true, MinimumSize = new Size((int)(90 * f), (int)(28 * f)), Margin = new Padding(4) };
                var copy = new Button { Text = L.T("copy"), AutoSize = true, MinimumSize = new Size((int)(90 * f), (int)(28 * f)), Margin = new Padding(4) };
                close.Click += (s, e) => dlg.Close();
                copy.Click += (s, e) => { try { Clipboard.SetText(box.Text); } catch { } };
                var chk = new CheckBox { Text = L.T("hideCredits"), AutoSize = true, Checked = hideCredits, Margin = new Padding(12, 8, 4, 4) };
                tip.SetToolTip(chk, L.T("hideCreditsTip"));
                chk.CheckedChanged += (s, e) =>
                {
                    hideCredits = chk.Checked;
                    box.Text = shownText();
                    box.SelectionStart = 0; box.SelectionLength = 0;
                    if (liveRefresh != null) liveRefresh();
                };
                bar.Controls.Add(close);
                bar.Controls.Add(copy);
                bar.Controls.Add(chk);
                var pad = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10, 10, 10, 0) };
                pad.Controls.Add(box);
                dlg.Controls.Add(pad);
                dlg.Controls.Add(bar);
                dlg.AcceptButton = close;
                dlg.CancelButton = close;
                dlg.Shown += (s, e) => { box.SelectionStart = 0; box.SelectionLength = 0; };
                dlg.ShowDialog(this);
            }
        }

        void RefreshKaraokeCell(Clip c)
        {
            int i = clips.IndexOf(c);
            if (i >= 0 && i < list.Items.Count) list.Items[i].SubItems[4].Text = KarText(c);
        }

        // ---------- playlists ----------
        static string F(double v) { return v.ToString("R", CultureInfo.InvariantCulture); }

        // The whole list (songs, trims, settings) as text. Playlist files and the automatic backup both use this.
        string BuildPlaylistText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("MTM1");
            sb.AppendLine("mode=" + cmbMode.SelectedIndex);
            sb.AppendLine("overlap=" + overlap.Value.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("fade=" + (chkFade.Checked ? "1" : "0"));
            foreach (var c in clips.ToList())
                sb.AppendLine("clip=" + string.Join("|", new[]
                {
                    c.Path, F(c.Start), F(c.End), c.Transpose.ToString(CultureInfo.InvariantCulture),
                    c.Speed.ToString(CultureInfo.InvariantCulture), F(c.OverlapAfter),
                    c.LyricEnc.ToString(CultureInfo.InvariantCulture),
                    // edited karaoke text: one entry per lyric event, "\u0002" = unchanged
                    c.LyricTexts == null ? "" : Convert.ToBase64String(Encoding.UTF8.GetBytes(
                        string.Join("\u0001", c.LyricTexts.Select(t => t ?? "\u0002")))),
                    c.Volume.ToString(CultureInfo.InvariantCulture),
                    // changed instruments: "channel:program,channel:program"
                    c.Programs == null ? "" : string.Join(",", c.Programs.Select(kv => kv.Key + ":" + kv.Value))
                }));
            return sb.ToString();
        }

        void SavePlaylist()
        {
            if (clips.Count == 0) return;
            using (var d = new SaveFileDialog { Filter = L.T("plFilter"), DefaultExt = "mtm", FileName = "playlist.mtm", OverwritePrompt = true })
            {
                if (lastSaveDir != null && Directory.Exists(lastSaveDir)) d.InitialDirectory = lastSaveDir;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    File.WriteAllText(d.FileName, BuildPlaylistText(), new UTF8Encoding(false));
                    try { lastSaveDir = Path.GetDirectoryName(d.FileName); } catch { }
                    SetStatus(L.T("plSaved", d.FileName));
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("saveFail"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
        }

        void LoadPlaylist()
        {
            using (var d = new OpenFileDialog { Filter = L.T("plFilter") })
            {
                if (lastSaveDir != null && Directory.Exists(lastSaveDir)) d.InitialDirectory = lastSaveDir;
                if (d.ShowDialog(this) != DialogResult.OK) return;
                string[] lines;
                try
                {
                    lines = File.ReadAllLines(d.FileName, Encoding.UTF8);
                    if (lines.Length == 0 || lines[0].Trim() != "MTM1") throw new InvalidDataException(L.T("plFail"));
                }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, L.T("plFail"), MessageBoxButtons.OK, MessageBoxIcon.Error); return; }

                if (clips.Count > 0 &&
                    MessageBox.Show(this, L.T("plReplace"), L.T("title"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                player.Stop();
                PushUndo("playlist");
                var missing = LoadPlaylistLines(lines);
                SetStatus(L.T("plLoaded", clips.Count));
                if (missing.Count > 0)
                    MessageBox.Show(this, L.T("plMissing") + "\n" + string.Join("\n", missing), L.T("loadFail"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // Replaces the current list with the songs of a playlist text. Returns the files that could not be loaded.
        List<string> LoadPlaylistLines(string[] lines)
        {
            var loaded = new List<Clip>();
            var missing = new List<string>();
            int mode = -1, fadeOpt = -1;
            decimal ov = -1;
            var inv = CultureInfo.InvariantCulture;
            foreach (var line in lines.Skip(1))
            {
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                string key = line.Substring(0, eq), val = line.Substring(eq + 1);
                if (key == "mode") { int m; if (int.TryParse(val, out m) && m >= 0 && m < cmbMode.Items.Count) mode = m; }
                else if (key == "overlap") { decimal o; if (decimal.TryParse(val, NumberStyles.Float, inv, out o)) ov = Math.Max(overlap.Minimum, Math.Min(overlap.Maximum, o)); }
                else if (key == "fade") fadeOpt = val == "0" ? 0 : 1;
                else if (key == "clip")
                {
                    var p = val.Split('|');
                    try
                    {
                        var c = MakeClip(p[0]);
                        var seq = c.Seq;
                        double v; int n;
                        if (p.Length > 1 && double.TryParse(p[1], NumberStyles.Float, inv, out v) && v >= 0) c.Start = v;
                        if (p.Length > 2 && double.TryParse(p[2], NumberStyles.Float, inv, out v) && v >= 0) c.End = v;
                        if (p.Length > 3 && int.TryParse(p[3], NumberStyles.Integer, inv, out n)) c.Transpose = Math.Max(-24, Math.Min(24, n));
                        if (p.Length > 4 && int.TryParse(p[4], NumberStyles.Integer, inv, out n)) c.Speed = Math.Max(25, Math.Min(200, n));
                        if (p.Length > 5 && double.TryParse(p[5], NumberStyles.Float, inv, out v)) c.OverlapAfter = v < 0 ? -1 : Math.Min(60, v);
                        if (c.Start >= c.Duration) c.Start = 0;
                        if (p.Length > 7 && p[7].Length > 0 && int.TryParse(p[6], NumberStyles.Integer, inv, out n))
                        {
                            try
                            {
                                var texts = Encoding.UTF8.GetString(Convert.FromBase64String(p[7])).Split('\u0001')
                                                    .Select(t => t == "\u0002" ? null : t).ToArray();
                                if (texts.Length == MidiOps.LyricEvents(seq).Count)
                                {
                                    c.LyricEnc = n;
                                    c.LyricTexts = texts;
                                    c.Edited = MidiOps.WithLyrics(seq, texts, n == 0 ? null : Encoding.GetEncoding(n));
                                }
                            }
                            catch (Exception) { }   // damaged text entry: keep the song with its original text
                        }
                        if (p.Length > 8 && int.TryParse(p[8], NumberStyles.Integer, inv, out n)) c.Volume = Math.Max(10, Math.Min(300, n));
                        if (p.Length > 9 && p[9].Length > 0)
                        {
                            var prog = new Dictionary<int, int>();
                            foreach (var pair in p[9].Split(','))
                            {
                                var kv = pair.Split(':');
                                int ch, pr;
                                if (kv.Length == 2 && int.TryParse(kv[0], out ch) && int.TryParse(kv[1], out pr) && ch >= 0 && ch < 16 && pr >= 0 && pr < 128) prog[ch] = pr;
                            }
                            if (prog.Count > 0) c.Programs = prog;
                        }
                        loaded.Add(c);
                    }
                    catch (Exception ex) { missing.Add(Path.GetFileName(p[0]) + ": " + Localize(ex)); }
                }
            }
            clips.Clear();
            clips.AddRange(loaded);
            undoSuppress++;   // the undo step for the whole load is recorded by the caller
            if (mode >= 0) cmbMode.SelectedIndex = mode;
            if (ov >= 0) overlap.Value = ov;
            if (fadeOpt >= 0) chkFade.Checked = fadeOpt == 1;
            undoSuppress--;
            dataVersion++;
            seekPos = 0;
            Refresh(clips.Count > 0 ? 0 : -1);
            return missing;
        }
    }
}
