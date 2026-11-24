using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows.Forms;

namespace MidiTool
{
    // Playback: play buttons, seek bar, "start/end here", live lyrics window.
    public partial class MainForm
    {
        readonly MidiPlayer player = new MidiPlayer();
        readonly Panel playerBar = new Panel();
        readonly TrackBar seekBar = new TrackBar();
        readonly Label lblPos = new Label();
        readonly Button btnSetStart = new Button();
        readonly Button btnSetEnd = new Button();
        readonly Button btnLive = new Button();
        readonly Timer seekTimer = new Timer { Interval = 100 };

        int playingWhich;        // 1 = selected song, 2 = merged result (while playing)
        int curWhich = 1;        // what the seek bar refers to
        double seekPos;          // position (seconds) when not playing
        bool seeking;            // the user is dragging the seek bar
        int lastSeekSel = -2;
        MidiSequence playSeq;    // what is being played
        double playTotal;
        int outDevice = -1;      // MIDI output device number, -1 = Windows default
        string outDeviceName;    // remembered between runs

        // prepared songs, cached until something changes (index 1 = selected song, 2 = merged result)
        readonly MidiSequence[] cacheSeq = new MidiSequence[3];
        readonly string[] cacheKey = new string[3];
        readonly double[] cacheTotal = new double[3];

        int SelIndex { get { return list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0]; } }

        void BuildPlayerBar()
        {
            foreach (var b in new[] { btnPlay, btnPlayAll, btnSetStart, btnSetEnd, btnLive }) SetupButton(b);
            btnPlay.Click += (s, e) => PlayClicked(1);
            btnPlayAll.Click += (s, e) => PlayClicked(2);
            btnSetStart.Click += (s, e) => SetTrimPoint(true);
            btnSetEnd.Click += (s, e) => SetTrimPoint(false);
            btnLive.Click += (s, e) => ShowLive();

            seekBar.Minimum = 0; seekBar.Maximum = 1000; seekBar.TickStyle = TickStyle.None;
            seekBar.AutoSize = false; seekBar.LargeChange = 50; seekBar.SmallChange = 10;
            seekBar.MouseDown += (s, e) =>
            {
                if (!seekBar.Enabled || e.Button != MouseButtons.Left) return;
                seeking = true;
                double r = (e.X - 12.0) / Math.Max(1, seekBar.Width - 24);   // jump to where it was clicked
                seekBar.Value = (int)Math.Max(0, Math.Min(1000, Math.Round(r * 1000)));
                ShowBarPosition();
            };
            seekBar.MouseUp += (s, e) => { if (seeking) { seeking = false; ApplySeek(); } };
            seekBar.Scroll += (s, e) => { if (seeking) ShowBarPosition(); else ApplySeek(); };

            lblPos.AutoSize = false;
            lblPos.TextAlign = ContentAlignment.MiddleRight;

            seekTimer.Tick += (s, e) => UpdateSeekUi();
            player.Finished += OnPlayerFinished;

            playerBar.Dock = DockStyle.Bottom;
            playerBar.Controls.AddRange(new Control[] { btnPlay, btnPlayAll, btnSetStart, btnSetEnd, btnLive, seekBar, lblPos });
            playerBar.Resize += (s, e) => LayoutPlayerBar();
            playerBar.Paint += (s, e) => e.Graphics.DrawLine(SystemPens.ControlDark, 0, 0, playerBar.Width, 0);
        }

        void FillPlayerBar()
        {
            btnSetStart.Text = L.T("setStart");
            btnSetEnd.Text = L.T("setEnd");
            btnLive.Text = L.T("live");
            tip.SetToolTip(btnSetStart, L.T("setStartTip"));
            tip.SetToolTip(btnSetEnd, L.T("setEndTip"));
            UpdatePlayButtons();
            LayoutPlayerBar();
        }

        // Row 1: play buttons (left), start/end here + live lyrics (right). Row 2: seek bar and time.
        void LayoutPlayerBar()
        {
            float f = TextScale;
            int h = Math.Max(Pref(btnPlay).Height, (int)(28 * f));
            int W = playerBar.ClientSize.Width;
            int row1 = 8;

            btnPlay.Left = 12; btnPlay.Top = row1;
            btnPlayAll.Left = btnPlay.Right + 8; btnPlayAll.Top = row1;
            btnLive.Top = btnSetEnd.Top = btnSetStart.Top = row1;
            btnLive.Left = W - Math.Max(Pref(btnLive).Width, btnLive.MinimumSize.Width) - 12;
            btnSetEnd.Left = btnLive.Left - 14 - Math.Max(Pref(btnSetEnd).Width, btnSetEnd.MinimumSize.Width);
            btnSetStart.Left = btnSetEnd.Left - 6 - Math.Max(Pref(btnSetStart).Width, btnSetStart.MinimumSize.Width);

            int row2 = row1 + h + 4;
            int lw = (int)(110 * f);
            lblPos.SetBounds(W - lw - 12, row2, lw, (int)(26 * f));
            seekBar.SetBounds(12, row2, Math.Max(50, W - lw - 36), (int)(26 * f));
            playerBar.Height = row2 + (int)(26 * f) + 8;
        }

        static string FmtT(double sec)
        {
            if (sec < 0) sec = 0;
            int t = (int)sec;
            return (t / 60) + ":" + (t % 60).ToString("00");
        }

        // ---------- prepared sequences ----------
        // The sequence for "selected song" (1) or "merged result" (2), cached until the data changes.
        MidiSequence GetSeq(int which, bool applyTrim)
        {
            if (which == 1)
            {
                if (Selected == null) return null;
                if (applyTrim && !ApplyTrim()) return null;   // use the values currently typed in the boxes
            }
            else if (clips.Count == 0) return null;
            string key = dataVersion + ":" + SelIndex;
            if (cacheSeq[which] != null && cacheKey[which] == key) return cacheSeq[which];
            var seq = which == 1 ? Prepare(Selected) : BuildMerged();
            cacheSeq[which] = seq;
            cacheKey[which] = key;
            cacheTotal[which] = MidiOps.DurationSeconds(seq);
            return seq;
        }

        // ---------- play / stop ----------
        void UpdatePlayButtons()
        {
            bool playing = player.IsPlaying;
            btnPlay.Text = playing && playingWhich == 1 ? L.T("stop") : L.T("play");
            btnPlayAll.Text = playing && playingWhich == 2 ? L.T("stop") : L.T("playAll");
            btnPlay.Enabled = Selected != null && (!playing || playingWhich == 1);
            btnPlayAll.Enabled = clips.Count > 0 && (!playing || playingWhich == 2);
            LayoutPlayerBar();
            UpdateSeekUi();
        }

        void PlayClicked(int which)
        {
            if (player.IsPlaying)
            {
                seekPos = Math.Min(player.Position, playTotal);   // stopping works as a pause
                player.Stop();
                seekTimer.Stop();
                UpdatePlayButtons();
                SetStatus(L.T("statusHelp"));
                return;
            }
            try
            {
                var seq = GetSeq(which, true);
                if (seq == null) return;
                double total = cacheTotal[which];
                if (which != curWhich) seekPos = 0;
                curWhich = which;
                lastSeekSel = SelIndex;
                if (seekPos >= total - 0.2) seekPos = 0;
                playSeq = seq; playTotal = total; playingWhich = which;
                player.Start(seq, seekPos, outDevice);
                seekTimer.Start();
                SetStatus(L.T("playing"));
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, L.T("playFail", Localize(ex)), L.T("title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            UpdatePlayButtons();
        }

        void OnPlayerFinished()
        {
            try
            {
                if (IsDisposed || !IsHandleCreated) return;
                BeginInvoke(new Action(() =>
                {
                    if (player.IsPlaying) return;   // a newer playback has already started
                    if (player.Position >= playTotal - 0.35) seekPos = 0;   // played to the end: rewind
                    seekTimer.Stop();
                    UpdatePlayButtons();
                    SetStatus(L.T("statusHelp"));
                }));
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            seekTimer.Stop();
            player.Stop();
            AutosaveNow();
            SaveSettings();
            base.OnFormClosing(e);
        }

        // ---------- seek bar ----------
        double BarTotal()
        {
            if (player.IsPlaying) return playTotal;
            if (curWhich == 1 && Selected == null) return 0;
            if (curWhich == 2 && clips.Count == 0) return 0;
            try { return GetSeq(curWhich, false) != null ? cacheTotal[curWhich] : 0; } catch { return 0; }
        }

        void ShowBarPosition()
        {
            double total = BarTotal();
            lblPos.Text = FmtT(seekBar.Value / 1000.0 * total) + " / " + FmtT(total);
        }

        // The user moved the seek bar: continue from there (or just remember the place if nothing is playing).
        void ApplySeek()
        {
            double total = BarTotal();
            if (total <= 0) return;
            seekPos = seekBar.Value / 1000.0 * total;
            if (player.IsPlaying)
            {
                try { player.Start(playSeq, Math.Min(seekPos, Math.Max(0, playTotal - 0.05)), outDevice); }
                catch (Exception ex) { MessageBox.Show(this, L.T("playFail", Localize(ex)), L.T("title"), MessageBoxButtons.OK, MessageBoxIcon.Error); }
            }
            UpdateSeekUi();
        }

        void UpdateSeekUi()
        {
            if (seeking) return;
            bool playing = player.IsPlaying;
            if (!playing && curWhich == 1 && SelIndex != lastSeekSel) { seekPos = 0; }   // another song: start from its beginning
            lastSeekSel = SelIndex;
            double total = BarTotal();
            double pos = playing ? Math.Min(player.Position, total) : Math.Min(seekPos, total);
            seekBar.Enabled = total > 0;
            seekBar.Value = total > 0 ? (int)Math.Max(0, Math.Min(1000, Math.Round(pos / total * 1000))) : 0;
            lblPos.Text = FmtT(pos) + " / " + FmtT(total);
            btnSetStart.Enabled = btnSetEnd.Enabled = curWhich == 1 && Selected != null && total > 0 && (!playing || playingWhich == 1);
        }

        // "Start here" / "End here": the current position becomes the trim point of the selected song.
        void SetTrimPoint(bool isStart)
        {
            var c = Selected;
            if (c == null || curWhich != 1) return;
            double pos = player.IsPlaying ? Math.Min(player.Position, playTotal) : seekPos;
            double orig = c.Start + pos * (c.Speed / 100.0);   // back to time in the original file
            orig = Math.Max(0, Math.Min(c.Duration, orig));
            if (isStart) SetTime(startMin, startSec, orig); else SetTime(endMin, endSec, orig);
            SetStatus(L.T(isStart ? "startSet" : "endSet", Fmt(orig)));
        }

        // ---------- live lyrics ----------
        Form liveForm;

        void ShowLive()
        {
            if (liveForm != null && !liveForm.IsDisposed) { liveForm.Activate(); return; }

            float f = TextScale;
            var area = Screen.FromControl(this).WorkingArea;
            var normal = new Font(Font.FontFamily, Font.Size * 2f);
            var bold = new Font(normal, FontStyle.Bold);
            Color sung = Color.FromArgb(31, 78, 140), now = Color.Crimson;

            var rtb = new RichTextBox
            {
                ReadOnly = true, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.White,
                Font = normal, HideSelection = false, DetectUrls = false, ScrollBars = RichTextBoxScrollBars.Vertical, TabStop = false
            };
            var form = new Form
            {
                Text = L.T("liveTitle"), Font = Font, Icon = Icon, ShowInTaskbar = false,
                StartPosition = FormStartPosition.CenterParent, MinimumSize = new Size(320, 240),
                ClientSize = new Size(Math.Min((int)(640 * f), area.Width - 40), Math.Min((int)(520 * f), area.Height - 100))
            };
            var chkHide = new CheckBox { Text = L.T("hideCredits"), AutoSize = true, Checked = hideCredits, Margin = new Padding(10, 6, 6, 6) };
            tip.SetToolTip(chkHide, L.T("hideCreditsTip"));
            var topBar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false };
            topBar.Controls.Add(chkHide);
            form.Controls.Add(rtb);
            form.Controls.Add(topBar);
            liveForm = form;

            MidiSequence shown = null;
            List<MidiOps.LyricSpan> spans = null;
            double[] times = null;
            int lastIdx = -2;
            var timer = new Timer { Interval = 60 };

            Action<int, int, Color, Font> paint = (start, len, color, font) =>
            {
                if (len <= 0) return;
                rtb.Select(start, len);
                rtb.SelectionColor = color;
                rtb.SelectionFont = font;
            };

            timer.Tick += (s, e) =>
            {
                MidiSequence seq = null;
                try { seq = player.IsPlaying ? playSeq : GetSeq(curWhich, false); } catch { }
                if (!ReferenceEquals(seq, shown))
                {
                    shown = seq;
                    spans = null; times = null; lastIdx = -2;
                    string text = "";
                    if (seq != null)
                    {
                        var c = curWhich == 1 ? Selected : null;
                        text = MidiOps.LyricsWithSpans(seq, c != null && c.LyricEnc != 0 ? Encoding.GetEncoding(c.LyricEnc) : null, out spans, hideCredits);
                        var tm = new TempoMap(seq);
                        times = spans.Select(sp => tm.TickToSec(sp.Tick)).ToArray();
                    }
                    rtb.Text = spans != null && spans.Count > 0 ? text : L.T("liveNone");
                    rtb.SelectAll(); rtb.SelectionColor = Color.Black; rtb.SelectionFont = normal; rtb.Select(0, 0);
                    if (spans == null || spans.Count == 0) { spans = null; return; }
                }
                if (spans == null) return;

                double pos = player.IsPlaying ? player.Position : seekPos;
                int idx = -1;   // last syllable that has started
                int lo = 0, hi = times.Length - 1;
                while (lo <= hi) { int mid = (lo + hi) / 2; if (times[mid] <= pos) { idx = mid; lo = mid + 1; } else hi = mid - 1; }
                if (idx == lastIdx) return;

                rtb.SuspendLayout();
                if (idx > lastIdx && lastIdx >= -1 && idx - lastIdx < 40)
                {
                    // moving forward: what was just sung turns blue, the new syllable is red
                    int from = lastIdx >= 0 ? spans[lastIdx].Start : 0;
                    paint(from, spans[idx].Start - from, sung, normal);
                    paint(spans[idx].Start, spans[idx].Length, now, bold);
                }
                else
                {
                    // jumped (seek) or first time: redo everything
                    rtb.SelectAll(); rtb.SelectionColor = Color.Black; rtb.SelectionFont = normal;
                    if (idx >= 0)
                    {
                        paint(0, spans[idx].Start, sung, normal);
                        paint(spans[idx].Start, spans[idx].Length, now, bold);
                    }
                }
                if (idx >= 0) { rtb.Select(spans[idx].Start + spans[idx].Length, 0); rtb.ScrollToCaret(); }
                else { rtb.Select(0, 0); rtb.ScrollToCaret(); }
                rtb.ResumeLayout();
                lastIdx = idx;
            };
            chkHide.CheckedChanged += (s, e) => { hideCredits = chkHide.Checked; shown = null; };   // next tick rebuilds the text
            liveRefresh = () => { chkHide.Checked = hideCredits; shown = null; };
            form.FormClosed += (s, e) => { timer.Stop(); timer.Dispose(); normal.Dispose(); bold.Dispose(); liveForm = null; liveRefresh = null; };
            timer.Start();
            form.Show(this);
        }
    }
}
