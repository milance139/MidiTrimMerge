using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MidiTool
{
    public class MidiEvent
    {
        public long Tick;
        public byte Status;     // 0x80-0xEF channel, 0xFF meta, 0xF0/0xF7 sysex
        public byte Meta;       // meta type when Status == 0xFF
        public byte D1, D2;     // channel message data
        public byte[] Data;     // meta / sysex payload

        public bool IsMeta { get { return Status == 0xFF; } }
        public bool IsSysex { get { return Status == 0xF0 || Status == 0xF7; } }
        public bool IsChannel { get { return Status >= 0x80 && Status < 0xF0; } }
        public int Type { get { return Status & 0xF0; } }
        public int Channel { get { return Status & 0x0F; } }
        public bool IsNoteOn { get { return IsChannel && Type == 0x90 && D2 > 0; } }
        public bool IsNoteOff { get { return IsChannel && (Type == 0x80 || (Type == 0x90 && D2 == 0)); } }
        public bool IsTempo { get { return IsMeta && Meta == 0x51 && Data != null && Data.Length == 3; } }
        // Tempo / time signature / key signature live in the conductor track
        public bool IsConductor { get { return IsMeta && (Meta == 0x51 || Meta == 0x58 || Meta == 0x59); } }
        public int NoteKey { get { return (Channel << 8) | D1; } }

        public MidiEvent Clone() { return (MidiEvent)MemberwiseClone(); }
    }

    public class MidiSequence
    {
        public int Ppq = 480;
        public List<List<MidiEvent>> Tracks = new List<List<MidiEvent>>();
        public long Length;   // in ticks

        /// <summary>True if the file really carries words to sing: a few lyric events left after the credit lines are taken out.</summary>
        public bool HasKaraoke
        {
            get
            {
                var words = MidiOps.WithoutCredits(MidiOps.LyricEvents(this), null);
                return words.Count(e => MidiOps.HasLetters(e.Data)) >= 3;
            }
        }
    }

    class TempoMap
    {
        readonly List<long> ticks = new List<long>();
        readonly List<int> uspq = new List<int>();
        readonly List<double> secAt = new List<double>();
        readonly int ppq;

        public TempoMap(MidiSequence s)
        {
            ppq = s.Ppq;
            var tempos = s.Tracks.SelectMany(t => t).Where(e => e.IsTempo).OrderBy(e => e.Tick).ToList();
            ticks.Add(0); uspq.Add(500000); secAt.Add(0);
            foreach (var e in tempos)
            {
                int v = (e.Data[0] << 16) | (e.Data[1] << 8) | e.Data[2];
                if (v <= 0) continue;
                if (e.Tick == ticks[ticks.Count - 1]) { uspq[uspq.Count - 1] = v; continue; }
                double sec = secAt[secAt.Count - 1] + (e.Tick - ticks[ticks.Count - 1]) * (uspq[uspq.Count - 1] / 1e6) / ppq;
                ticks.Add(e.Tick); uspq.Add(v); secAt.Add(sec);
            }
        }

        int Seg(long tick)
        {
            int i = ticks.Count - 1;
            while (i > 0 && ticks[i] > tick) i--;
            return i;
        }

        public int TempoAt(long tick) { return uspq[Seg(tick)]; }

        public double TickToSec(long tick)
        {
            int i = Seg(tick);
            return secAt[i] + (tick - ticks[i]) * (uspq[i] / 1e6) / ppq;
        }

        public long SecToTick(double sec)
        {
            int i = secAt.Count - 1;
            while (i > 0 && secAt[i] > sec) i--;
            return ticks[i] + (long)Math.Round((sec - secAt[i]) * ppq / (uspq[i] / 1e6));
        }
    }

    public static class MidiIO
    {
        static int BE16(byte[] b, int p) { return (b[p] << 8) | b[p + 1]; }
        static int BE32(byte[] b, int p) { return (b[p] << 24) | (b[p + 1] << 16) | (b[p + 2] << 8) | b[p + 3]; }

        static long ReadVlq(byte[] b, ref int p, int end)
        {
            long v = 0;
            for (int i = 0; i < 4 && p < end; i++)
            {
                byte c = b[p++];
                v = (v << 7) | (uint)(c & 0x7F);
                if ((c & 0x80) == 0) break;
            }
            return v;
        }

        public static MidiSequence Load(string path)
        {
            byte[] b = File.ReadAllBytes(path);
            int start = 0;
            // RIFF RMID wrapper
            if (b.Length > 20 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F')
            {
                for (int i = 12; i + 4 < b.Length; i++)
                    if (b[i] == 'M' && b[i + 1] == 'T' && b[i + 2] == 'h' && b[i + 3] == 'd') { start = i; break; }
            }
            if (b.Length < start + 14 || b[start] != 'M' || b[start + 1] != 'T' || b[start + 2] != 'h' || b[start + 3] != 'd')
                throw new InvalidDataException("Not a standard MIDI file.");

            int hdrLen = BE32(b, start + 4);
            int ntr = BE16(b, start + 10);
            int div = BE16(b, start + 12);
            if ((div & 0x8000) != 0) throw new NotSupportedException("SMPTE time-division MIDI files are not supported.");
            if (div == 0) throw new InvalidDataException("Invalid time division.");

            var seq = new MidiSequence { Ppq = div };
            int p = start + 8 + hdrLen;
            long maxTick = 0;
            while (p + 8 <= b.Length && seq.Tracks.Count < ntr)
            {
                string id = "" + (char)b[p] + (char)b[p + 1] + (char)b[p + 2] + (char)b[p + 3];
                int len = BE32(b, p + 4);
                p += 8;
                int end = (len < 0 || p + len > b.Length) ? b.Length : p + len;
                if (id == "MTrk")
                {
                    var track = ParseTrack(b, p, end, ref maxTick);
                    seq.Tracks.Add(track);
                }
                p = end;
            }
            if (seq.Tracks.Count == 0) throw new InvalidDataException("The file contains no tracks.");
            seq.Length = maxTick;
            return seq;
        }

        static List<MidiEvent> ParseTrack(byte[] b, int p, int end, ref long maxTick)
        {
            var list = new List<MidiEvent>();
            long tick = 0;
            byte running = 0;
            while (p < end)
            {
                tick += ReadVlq(b, ref p, end);
                if (p >= end) break;
                byte s = b[p];
                if (s < 0x80)
                {
                    if (running == 0) break;
                    s = running;
                }
                else
                {
                    p++;
                    running = (s < 0xF0) ? s : (byte)0;
                }

                if (s == 0xFF)
                {
                    if (p >= end) break;
                    byte type = b[p++];
                    int len = (int)ReadVlq(b, ref p, end);
                    if (len < 0 || p + len > end) break;
                    var data = new byte[len];
                    Array.Copy(b, p, data, 0, len);
                    p += len;
                    if (tick > maxTick) maxTick = tick;
                    if (type == 0x2F) continue;  // end of track is regenerated on save
                    list.Add(new MidiEvent { Tick = tick, Status = 0xFF, Meta = type, Data = data });
                }
                else if (s == 0xF0 || s == 0xF7)
                {
                    int len = (int)ReadVlq(b, ref p, end);
                    if (len < 0 || p + len > end) break;
                    var data = new byte[len];
                    Array.Copy(b, p, data, 0, len);
                    p += len;
                    list.Add(new MidiEvent { Tick = tick, Status = s, Data = data });
                }
                else
                {
                    int n = ((s & 0xF0) == 0xC0 || (s & 0xF0) == 0xD0) ? 1 : 2;
                    if (p + n > end) break;
                    var e = new MidiEvent { Tick = tick, Status = s, D1 = b[p++] };
                    if (n == 2) e.D2 = b[p++];
                    list.Add(e);
                    if (tick > maxTick) maxTick = tick;
                }
            }
            return list;
        }

        static void WriteVlq(Stream s, long v)
        {
            var buf = new Stack<byte>();
            buf.Push((byte)(v & 0x7F));
            v >>= 7;
            while (v > 0) { buf.Push((byte)((v & 0x7F) | 0x80)); v >>= 7; }
            foreach (var x in buf) s.WriteByte(x);
        }

        static void WriteBE(Stream s, int v, int bytes)
        {
            for (int i = bytes - 1; i >= 0; i--) s.WriteByte((byte)(v >> (8 * i)));
        }

        public static void Save(MidiSequence seq, string path)
        {
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                fs.Write(new byte[] { (byte)'M', (byte)'T', (byte)'h', (byte)'d' }, 0, 4);
                WriteBE(fs, 6, 4);
                WriteBE(fs, seq.Tracks.Count > 1 ? 1 : 0, 2);
                WriteBE(fs, seq.Tracks.Count, 2);
                WriteBE(fs, seq.Ppq, 2);

                foreach (var track in seq.Tracks)
                {
                    var ms = new MemoryStream();
                    long last = 0;
                    foreach (var e in MidiOps.OrderForPlayback(track))
                    {
                        WriteVlq(ms, e.Tick - last);
                        last = e.Tick;
                        if (e.IsMeta)
                        {
                            ms.WriteByte(0xFF); ms.WriteByte(e.Meta);
                            WriteVlq(ms, e.Data.Length);
                            ms.Write(e.Data, 0, e.Data.Length);
                        }
                        else if (e.IsSysex)
                        {
                            ms.WriteByte(e.Status);
                            WriteVlq(ms, e.Data.Length);
                            ms.Write(e.Data, 0, e.Data.Length);
                        }
                        else
                        {
                            ms.WriteByte(e.Status);
                            ms.WriteByte(e.D1);
                            if (e.Type != 0xC0 && e.Type != 0xD0) ms.WriteByte(e.D2);
                        }
                    }
                    WriteVlq(ms, Math.Max(0, seq.Length - last));
                    ms.WriteByte(0xFF); ms.WriteByte(0x2F); ms.WriteByte(0);

                    fs.Write(new byte[] { (byte)'M', (byte)'T', (byte)'r', (byte)'k' }, 0, 4);
                    WriteBE(fs, (int)ms.Length, 4);
                    ms.WriteTo(fs);
                }
            }
        }
    }

    public static class MidiOps
    {
        /// <summary>
        /// Events in time order (otherwise keeping their order). At the same tick, note-offs of earlier notes go first so a
        /// repeated note is not cut short - but a note that starts and ends on that very tick keeps its on-before-off order
        /// (otherwise it would never stop).
        /// </summary>
        public static List<MidiEvent> OrderForPlayback(IEnumerable<MidiEvent> events)
        {
            var res = new List<MidiEvent>();
            foreach (var g in events.GroupBy(e => e.Tick).OrderBy(g => g.Key))
            {
                var list = g.ToList();
                if (list.Count == 1) { res.Add(list[0]); continue; }
                var openHere = new HashSet<int>();
                var first = new List<MidiEvent>();
                var rest = new List<MidiEvent>();
                foreach (var e in list)
                {
                    if (e.IsNoteOn) { openHere.Add(e.NoteKey); rest.Add(e); }
                    else if (e.IsNoteOff && openHere.Remove(e.NoteKey)) rest.Add(e);   // ends a note that began on this tick
                    else if (e.IsNoteOff) first.Add(e);
                    else rest.Add(e);
                }
                res.AddRange(first);
                res.AddRange(rest);
            }
            return res;
        }

        public static double DurationSeconds(MidiSequence s)
        {
            return new TempoMap(s).TickToSec(s.Length);
        }

        /// <summary>
        /// Cuts the sequence to [startSec, endSec). endSec &lt;= 0 means "to the end".
        /// Notes that are sounding at the cut points are started/stopped cleanly, and the
        /// tempo, programs, controllers and pitch-bend in effect at the start are preserved.
        /// </summary>
        public static MidiSequence Trim(MidiSequence src, double startSec, double endSec)
        {
            var tm = new TempoMap(src);
            double total = tm.TickToSec(src.Length);
            long st = startSec <= 0 ? 0 : tm.SecToTick(startSec);
            long en = (endSec <= 0 || endSec >= total) ? src.Length : tm.SecToTick(endSec);
            if (st >= src.Length) throw new ArgumentException("Start is beyond the end of the file.");
            if (en <= st) throw new ArgumentException("End must be after start.");

            var res = new MidiSequence { Ppq = src.Ppq, Length = en - st };

            for (int ti = 0; ti < src.Tracks.Count; ti++)
            {
                var outTrack = new List<MidiEvent>();
                var ordered = src.Tracks[ti].OrderBy(e => e.Tick).ToList();
                var latestMeta = new Dictionary<byte, MidiEvent>();
                var state = new Dictionary<int, MidiEvent>();
                var active = new Dictionary<int, MidiEvent>();

                if (ti == 0)
                {
                    outTrack.Add(new MidiEvent
                    {
                        Tick = 0, Status = 0xFF, Meta = 0x51,
                        Data = new[] { (byte)(tm.TempoAt(st) >> 16), (byte)(tm.TempoAt(st) >> 8), (byte)tm.TempoAt(st) }
                    });
                }

                int i = 0;
                for (; i < ordered.Count && ordered[i].Tick < st; i++)
                {
                    var e = ordered[i];
                    if (e.IsMeta)
                    {
                        if (e.Meta == 0x03 || e.Meta == 0x04 || e.Meta == 0x58 || e.Meta == 0x59) latestMeta[e.Meta] = e;
                    }
                    else if (e.IsNoteOn) active[e.NoteKey] = e;
                    else if (e.IsNoteOff) active.Remove(e.NoteKey);
                    else if (e.IsChannel)
                    {
                        int key = (e.Channel << 16) | (e.Type << 8) | (e.Type == 0xB0 ? e.D1 : 0);
                        state[key] = e;
                    }
                }
                foreach (var e in latestMeta.Values) { var c = e.Clone(); c.Tick = 0; outTrack.Add(c); }
                foreach (var e in state.Values) { var c = e.Clone(); c.Tick = 0; outTrack.Add(c); }
                foreach (var e in active.Values) { var c = e.Clone(); c.Tick = 0; outTrack.Add(c); }

                for (; i < ordered.Count && ordered[i].Tick <= en; i++)
                {
                    var e = ordered[i];
                    if (e.Tick == en && !e.IsNoteOff) continue;
                    if (e.IsTempo && e.Tick == st) continue;   // already emitted at tick 0
                    if (e.IsNoteOn) active[e.NoteKey] = e;
                    else if (e.IsNoteOff) active.Remove(e.NoteKey);
                    var c = e.Clone();
                    c.Tick = e.Tick - st;
                    outTrack.Add(c);
                }
                foreach (var e in active.Values)
                    outTrack.Add(new MidiEvent { Tick = en - st, Status = (byte)(0x80 | e.Channel), D1 = e.D1, D2 = 0 });

                res.Tracks.Add(outTrack);
            }
            if (res.Tracks.Count == 0) res.Tracks.Add(new List<MidiEvent>());
            return res;
        }

        static MidiSequence Rescale(MidiSequence s, int ppq)
        {
            if (s.Ppq == ppq) return s;
            double f = (double)ppq / s.Ppq;
            var r = new MidiSequence { Ppq = ppq, Length = (long)Math.Round(s.Length * f) };
            foreach (var t in s.Tracks)
            {
                var nt = new List<MidiEvent>(t.Count);
                foreach (var e in t) { var c = e.Clone(); c.Tick = (long)Math.Round(e.Tick * f); nt.Add(c); }
                r.Tracks.Add(nt);
            }
            return r;
        }

        /// <summary>
        /// Plays the clips one after another. With overlapSec &gt; 0 each clip starts that many
        /// seconds before the previous one ends (limited to the length of the shorter of the two).
        /// </summary>
        public static MidiSequence Concatenate(IList<MidiSequence> clips, double overlapSec = 0)
        {
            return Concatenate(clips, Enumerable.Repeat(overlapSec, Math.Max(0, clips.Count - 1)).ToList());
        }

        /// <summary>
        /// Same, but with its own overlap (seconds) for every joint: overlaps[i] is between clip i and clip i+1.
        /// During an overlap the new song's timing is kept exactly (even if its tempo differs from the old song's);
        /// with fade = true the old song fades out and the new one fades in.
        /// </summary>
        public static MidiSequence Concatenate(IList<MidiSequence> clips, IList<double> overlaps, bool fade = false)
        {
            int ppq = clips[0].Ppq;
            var res = new MidiSequence { Ppq = ppq };
            var slots = new List<List<MidiEvent>> { new List<MidiEvent>() };  // slot 0 = conductor
            var fadeTrack = new List<MidiEvent>();
            long offset = 0;   // end of the previous clip
            MidiSequence prev = null;
            Dictionary<int, int> prevMap = null;
            var prevExpr = new Dictionary<int, List<KeyValuePair<long, int>>>();
            for (int k = 0; k < clips.Count; k++)
            {
                var clip = Rescale(clips[k], ppq);
                double overlapSec = (k > 0 && k - 1 < overlaps.Count) ? overlaps[k - 1] : 0;
                long ov = 0;    // length of the overlap in merged ticks (= ticks of the old song's tail)
                long cut = 0;   // the same moment in the new song's own ticks
                double o = 0;   // overlap in seconds
                TempoMap ctm = null;
                if (prev != null && overlapSec > 0)
                {
                    double prevDur = DurationSeconds(prev), curDur = DurationSeconds(clip);
                    o = Math.Min(overlapSec, Math.Min(prevDur, curDur));
                    long tail = prev.Length - new TempoMap(prev).SecToTick(prevDur - o);
                    ctm = new TempoMap(clip);
                    ov = Math.Max(0, Math.Min(tail, prev.Length));
                    cut = Math.Max(0, Math.Min(ctm.SecToTick(o), clip.Length));
                    if (ov <= 0 || cut <= 0 || o <= 0) { ov = 0; cut = 0; o = 0; ctm = null; }
                }
                long start = offset - ov;
                long ovF = ov, cutF = cut, startF = start; double oF = o; var ctmF = ctm;
                // where a tick of the new song lands in the merged file
                Func<long, long> place = tick =>
                {
                    if (ovF <= 0) return startF + tick;
                    if (tick < cutF) return startF + Math.Min(ovF, (long)Math.Round(ovF * (ctmF.TickToSec(tick) / oF)));   // inside the overlap: keep real timing
                    return startF + ovF + (tick - cutF);
                };

                // Crossfade: the two songs must not use the same MIDI channel while both sound (their volume/instrument
                // settings would clash and a fade of one would fade the other), so the new song's clashing channels move to free ones.
                var curCh = UsedChannels(clip);
                var map = new Dictionary<int, int>();
                foreach (var ch in curCh) map[ch] = ch;
                var shared = new HashSet<int>();   // channels that could not be moved: no fade for them
                var fadeOut = new List<int>();
                var fadeIn = new List<int>();
                if (fade && ov > 0)
                {
                    var prevAct = ActiveChannels(prev, prev.Length - ov, long.MaxValue, prevMap);   // sounding at the end of the old song
                    var curAct = ActiveChannels(clip, 0, cut, null);                                 // sounding at the start of the new one
                    var used = new HashSet<int>(prevAct.Concat(curCh));
                    foreach (var ch in curCh.Where(prevAct.Contains).ToList())
                    {
                        int free = -1;
                        for (int x = 0; x < 16; x++) if (x != 9 && !used.Contains(x)) { free = x; break; }
                        if (free >= 0) { map[ch] = free; used.Add(free); } else shared.Add(ch);
                    }
                    fadeOut.AddRange(prevAct.Where(x => !shared.Contains(x)));
                    fadeIn.AddRange(curAct.Select(x => map[x]).Where(x => !shared.Contains(x)).Distinct());
                }

                var curExpr = new Dictionary<int, List<KeyValuePair<long, int>>>();
                MidiEvent pendingTempo = null;   // a tempo change inside the overlap would speed up/slow down the old song's tail
                for (int ti = 0; ti < clip.Tracks.Count; ti++)
                {
                    while (slots.Count <= ti + 1) slots.Add(new List<MidiEvent>());
                    foreach (var e in clip.Tracks[ti])
                    {
                        if (e.IsTempo && ov > 0 && e.Tick < cut)
                        {
                            if (pendingTempo == null || e.Tick >= pendingTempo.Tick) pendingTempo = e;
                            continue;
                        }
                        var c = e.Clone();
                        c.Tick = place(e.Tick);
                        int mc;
                        if (c.IsChannel && c.Channel != 9 && map.TryGetValue(c.Channel, out mc) && mc != c.Channel)
                            c.Status = (byte)((c.Status & 0xF0) | mc);
                        if (c.IsChannel && c.Channel != 9 && c.Type == 0xB0 && c.D1 == 11)
                        {
                            List<KeyValuePair<long, int>> l;
                            if (!curExpr.TryGetValue(c.Channel, out l)) curExpr[c.Channel] = l = new List<KeyValuePair<long, int>>();
                            l.Add(new KeyValuePair<long, int>(e.Tick, c.D2));
                        }
                        (e.IsConductor ? slots[0] : slots[ti + 1]).Add(c);
                    }
                }
                if (pendingTempo != null)
                {
                    var c = pendingTempo.Clone();
                    c.Tick = start + ov;   // the new tempo takes effect once the old song is over
                    slots[0].Add(c);
                }
                if (fade && ov > 0)
                {
                    const int N = 16;
                    foreach (var ch in fadeOut)
                    {
                        int b = ExprAt(prevExpr, ch, prev.Length - ov);
                        for (int i = 1; i < N; i++) fadeTrack.Add(Cc(start + ov * i / N, ch, 11, b * (N - i) / N));
                        fadeTrack.Add(Cc(start + ov + 1, ch, 11, b));   // restore once the old song is over
                    }
                    foreach (var ch in fadeIn)
                    {
                        int b = ExprAt(curExpr, ch, cut);
                        fadeTrack.Add(Cc(Math.Max(0, start - 1), ch, 11, 0));
                        for (int i = 1; i <= N; i++) fadeTrack.Add(Cc(start + ov * i / N, ch, 11, b * i / N));
                    }
                }
                offset = place(clip.Length);
                prev = clip;
                prevMap = map;
                prevExpr = curExpr;
            }
            if (fadeTrack.Count > 0) slots.Add(fadeTrack);
            res.Length = offset;
            res.Tracks = slots.Where((s, idx) => idx == 0 || s.Count > 0).ToList();
            return res;
        }

        // Melodic channels that have note events in [from, to] (channel numbers after applying map, if any).
        static List<int> ActiveChannels(MidiSequence s, long from, long to, Dictionary<int, int> map)
        {
            var h = new SortedSet<int>();
            foreach (var t in s.Tracks)
                foreach (var e in t)
                    if ((e.IsNoteOn || e.IsNoteOff) && e.Channel != 9 && e.Tick >= from && e.Tick <= to)
                    {
                        int m;
                        h.Add(map != null && map.TryGetValue(e.Channel, out m) ? m : e.Channel);
                    }
            return h.ToList();
        }

        static HashSet<int> UsedChannels(MidiSequence s)
        {
            var h = new HashSet<int>();
            foreach (var t in s.Tracks) foreach (var e in t) if (e.IsChannel && e.Channel != 9) h.Add(e.Channel);
            return h;
        }

        // Expression (CC11) value in effect on a channel at a tick (127 if none was set).
        static int ExprAt(Dictionary<int, List<KeyValuePair<long, int>>> d, int ch, long tick)
        {
            List<KeyValuePair<long, int>> l;
            if (!d.TryGetValue(ch, out l)) return 127;
            int v = 127; long best = -1;
            foreach (var p in l) if (p.Key <= tick && p.Key >= best) { best = p.Key; v = p.Value; }
            return v;
        }

        static MidiEvent Cc(long tick, int ch, int cc, int value)
        {
            return new MidiEvent { Tick = tick, Status = (byte)(0xB0 | ch), D1 = (byte)cc, D2 = (byte)Math.Max(0, Math.Min(127, value)) };
        }

        /// <summary>General MIDI program in use on each melodic channel that plays notes (first program change, else 0).</summary>
        public static SortedDictionary<int, int> ProgramsUsed(MidiSequence s)
        {
            var res = new SortedDictionary<int, int>();
            foreach (var e in s.Tracks.SelectMany(t => t)) if (e.IsNoteOn && e.Channel != 9 && !res.ContainsKey(e.Channel)) res[e.Channel] = 0;
            foreach (var ch in res.Keys.ToList())
            {
                var pc = s.Tracks.SelectMany(t => t).Where(e => e.IsChannel && e.Channel == ch && e.Type == 0xC0).OrderBy(e => e.Tick).FirstOrDefault();
                if (pc != null) res[ch] = pc.D1;
            }
            return res;
        }

        /// <summary>Forces the given instrument (program) on a channel for the whole song. map: channel -> program.</summary>
        public static MidiSequence ChangeInstruments(MidiSequence src, IDictionary<int, int> map)
        {
            var res = new MidiSequence { Ppq = src.Ppq, Length = src.Length };
            foreach (var t in src.Tracks)
                res.Tracks.Add(t.Where(e => !(e.IsChannel && e.Type == 0xC0 && map.ContainsKey(e.Channel))).Select(e => e.Clone()).ToList());
            if (res.Tracks.Count == 0) res.Tracks.Add(new List<MidiEvent>());
            foreach (var kv in map)
                res.Tracks[0].Add(new MidiEvent { Tick = 0, Status = (byte)(0xC0 | kv.Key), D1 = (byte)kv.Value });
            return res;
        }

        /// <summary>Scales note velocities (volume of the song). factor 1 = unchanged.</summary>
        public static MidiSequence ScaleVelocity(MidiSequence src, double factor)
        {
            var res = new MidiSequence { Ppq = src.Ppq, Length = src.Length };
            foreach (var t in src.Tracks)
            {
                var nt = new List<MidiEvent>(t.Count);
                foreach (var e in t)
                {
                    var c = e.Clone();
                    if (c.IsNoteOn) c.D2 = (byte)Math.Max(1, Math.Min(127, (int)Math.Round(c.D2 * factor)));
                    nt.Add(c);
                }
                res.Tracks.Add(nt);
            }
            return res;
        }

        /// <summary>Average loudness of the notes (velocity x channel volume x expression, 0-127). 0 if there are no notes.</summary>
        public static double MeasureLoudness(MidiSequence s)
        {
            var vol = new int[16]; var expr = new int[16];
            for (int i = 0; i < 16; i++) { vol[i] = 100; expr[i] = 127; }
            double sum = 0; int n = 0;
            foreach (var e in s.Tracks.SelectMany(t => t).Where(e => e.IsChannel).OrderBy(e => e.Tick))
            {
                if (e.Type == 0xB0 && e.D1 == 7) vol[e.Channel] = e.D2;
                else if (e.Type == 0xB0 && e.D1 == 11) expr[e.Channel] = e.D2;
                else if (e.IsNoteOn) { sum += e.D2 * (vol[e.Channel] / 127.0) * (expr[e.Channel] / 127.0); n++; }
            }
            return n == 0 ? 0 : sum / n;
        }

        /// <summary>First tempo of the file in beats per minute.</summary>
        public static double FirstBpm(MidiSequence s)
        {
            return 60000000.0 / new TempoMap(s).TempoAt(0);
        }

        /// <summary>Key of the file: tonic pitch class (0 = C) and major/minor. Uses the key signature, otherwise estimates from the notes.</summary>
        public static bool GuessKey(MidiSequence s, out int tonic, out bool minor, out bool estimated)
        {
            tonic = 0; minor = false; estimated = false;
            var ks = s.Tracks.SelectMany(t => t).Where(e => e.IsMeta && e.Meta == 0x59 && e.Data != null && e.Data.Length == 2).OrderBy(e => e.Tick).FirstOrDefault();
            if (ks != null)
            {
                int sf = (sbyte)ks.Data[0];
                minor = ks.Data[1] == 1;
                tonic = (((sf * 7) % 12) + 12) % 12;
                if (minor) tonic = (tonic + 9) % 12;
                return true;
            }
            // Krumhansl-Schmuckler: compare the pitch-class histogram with the typical major / minor profiles
            var hist = new double[12];
            foreach (var e in s.Tracks.SelectMany(t => t)) if (e.IsNoteOn && e.Channel != 9) hist[e.D1 % 12]++;
            if (hist.Sum() < 8) return false;
            double[] maj = { 6.35, 2.23, 3.48, 2.33, 4.38, 4.09, 2.52, 5.19, 2.39, 3.66, 2.29, 2.88 };
            double[] min = { 6.33, 2.68, 3.52, 5.38, 2.60, 3.53, 2.54, 4.75, 3.98, 2.69, 3.34, 3.17 };
            double best = double.MinValue;
            for (int t = 0; t < 12; t++)
                for (int m = 0; m < 2; m++)
                {
                    var prof = m == 0 ? maj : min;
                    var rot = new double[12];
                    for (int i = 0; i < 12; i++) rot[i] = prof[(i - t + 12) % 12];
                    double r = Corr(hist, rot);
                    if (r > best) { best = r; tonic = t; minor = m == 1; }
                }
            estimated = true;
            return true;
        }

        static double Corr(double[] a, double[] b)
        {
            double ma = a.Average(), mb = b.Average(), sab = 0, saa = 0, sbb = 0;
            for (int i = 0; i < a.Length; i++) { sab += (a[i] - ma) * (b[i] - mb); saa += (a[i] - ma) * (a[i] - ma); sbb += (b[i] - mb) * (b[i] - mb); }
            return saa == 0 || sbb == 0 ? 0 : sab / Math.Sqrt(saa * sbb);
        }

        public class LyricSpan { public long Tick; public int Start; public int Length; }

        /// <summary>The words as plain text (newline line breaks) plus, for every syllable, where it sits in that text and when it is sung.</summary>
        public static string LyricsWithSpans(MidiSequence s, Encoding enc, out List<LyricSpan> spans, bool hideCredits = false)
        {
            spans = new List<LyricSpan>();
            var sb = new StringBuilder();
            var src = LyricEvents(s);
            if (hideCredits) src = WithoutCredits(src, enc);
            foreach (var e in src)
            {
                string t = DecodeText(e.Data, enc).Replace("\r\n", "\n").Replace('\r', '\n');
                if (t.StartsWith("\\")) { if (sb.Length > 0) sb.Append("\n\n"); t = t.Substring(1); }
                else if (t.StartsWith("/")) { if (sb.Length > 0) sb.Append('\n'); t = t.Substring(1); }
                spans.Add(new LyricSpan { Tick = e.Tick, Start = sb.Length, Length = t.Length });
                sb.Append(t);
            }
            return sb.ToString();
        }

        /// <summary>Shifts every note by the given number of semitones (drums on channel 10 are left alone).</summary>
        public static MidiSequence Transpose(MidiSequence src, int semitones)
        {
            var res = new MidiSequence { Ppq = src.Ppq, Length = src.Length };
            foreach (var t in src.Tracks)
            {
                var nt = new List<MidiEvent>(t.Count);
                foreach (var e in t)
                {
                    var c = e.Clone();
                    if (c.IsChannel && c.Channel != 9 && (c.Type == 0x80 || c.Type == 0x90 || c.Type == 0xA0))
                    {
                        int n = c.D1 + semitones;
                        while (n < 0) n += 12;
                        while (n > 127) n -= 12;
                        c.D1 = (byte)n;
                    }
                    nt.Add(c);
                }
                res.Tracks.Add(nt);
            }
            return res;
        }

        /// <summary>Plays faster (factor &gt; 1) or slower (factor &lt; 1) by scaling the tempo.</summary>
        public static MidiSequence ChangeSpeed(MidiSequence src, double factor)
        {
            var res = new MidiSequence { Ppq = src.Ppq, Length = src.Length };
            foreach (var t in src.Tracks)
            {
                var nt = new List<MidiEvent>(t.Count);
                foreach (var e in t)
                {
                    var c = e.Clone();
                    if (c.IsTempo)
                    {
                        int v = (c.Data[0] << 16) | (c.Data[1] << 8) | c.Data[2];
                        v = (int)Math.Max(1, Math.Min(0xFFFFFF, Math.Round(v / factor)));
                        c.Data = new[] { (byte)(v >> 16), (byte)(v >> 8), (byte)v };
                    }
                    nt.Add(c);
                }
                res.Tracks.Add(nt);
            }
            return res;
        }

        /// <summary>Time (seconds) of the first note start and of the last note event. False if there are no notes.</summary>
        public static bool FindNoteSpan(MidiSequence s, out double first, out double last)
        {
            first = last = 0;
            long min = long.MaxValue, max = -1;
            foreach (var t in s.Tracks)
                foreach (var e in t)
                {
                    if (e.IsNoteOn && e.Tick < min) min = e.Tick;
                    if ((e.IsNoteOn || e.IsNoteOff) && e.Tick > max) max = e.Tick;
                }
            if (max < 0 || min == long.MaxValue) return false;
            var tm = new TempoMap(s);
            first = tm.TickToSec(min);
            last = tm.TickToSec(max);
            return true;
        }

        /// <summary>enc == null means automatic: UTF-8 if the bytes are valid UTF-8, otherwise Windows-1250.</summary>
        public static string DecodeText(byte[] d, Encoding enc = null)
        {
            if (enc != null) return enc.GetString(d);
            try { return new UTF8Encoding(false, true).GetString(d); }
            catch (Exception) { return Encoding.GetEncoding(1250).GetString(d); }
        }

        /// <summary>enc == null means automatic: Windows-1250 if every character fits, otherwise UTF-8.</summary>
        public static byte[] EncodeText(string t, Encoding enc = null)
        {
            if (enc != null) return enc.GetBytes(t);
            try { return Encoding.GetEncoding(1250, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback).GetBytes(t); }
            catch (EncoderFallbackException) { return new UTF8Encoding(false).GetBytes(t); }
        }

        /// <summary>The events that carry the words (Lyric events, or .kar text events), in time order.</summary>
        public static List<MidiEvent> LyricEvents(MidiSequence s)
        {
            var evs = s.Tracks.SelectMany(t => t).Where(e => e.IsMeta && e.Data != null && e.Data.Length > 0)
                       .OrderBy(e => e.Tick).ToList();
            var src = evs.Where(e => e.Meta == 0x05).ToList();
            if (src.Count == 0) src = evs.Where(e => e.Meta == 0x01 && e.Data[0] != (byte)'@').ToList();
            return src;
        }

        /// <summary>
        /// Copy of the sequence with new words. texts[i] replaces the text of LyricEvents(s)[i];
        /// a null entry leaves that event as it was.
        /// </summary>
        public static MidiSequence WithLyrics(MidiSequence s, IList<string> texts, Encoding enc)
        {
            var map = new Dictionary<MidiEvent, string>();
            var evs = LyricEvents(s);
            for (int i = 0; i < evs.Count && i < texts.Count; i++)
                if (texts[i] != null) map[evs[i]] = texts[i];
            var res = new MidiSequence { Ppq = s.Ppq, Length = s.Length };
            foreach (var t in s.Tracks)
            {
                var nt = new List<MidiEvent>(t.Count);
                foreach (var e in t)
                {
                    var c = e.Clone();
                    string txt;
                    if (map.TryGetValue(e, out txt)) c.Data = EncodeText(txt, enc);
                    nt.Add(c);
                }
                res.Tracks.Add(nt);
            }
            return res;
        }

        static readonly string[] CreditMarks =
        {
            "rights reserved", "copyright", "©", "®", "(c)", "phone", "tel:", "tel.", "e-mail", "email", "mail:",
            "http", "www", "@", "midi from", "collection", "distributed by", "sequenced by", "created by", "written by", "generated by",
            "arranged by", "music by", "lyrics by", "words by", "music:", "lyrics:", "midified", "gnmidi", "cromidi", "kmidi", "midifajlovi", "gmail", "hotmail", "yahoo"
        };

        static bool IsBlankText(string t) { return !t.Any(char.IsLetterOrDigit); }

        // Bits of a phone number that are split over several events: no letters, at least two digits ("(0)91", "501", "72").
        static bool IsNumberBit(string t) { return !t.Any(char.IsLetter) && t.Count(char.IsDigit) >= 2; }

        public static bool HasLetters(byte[] data) { return DecodeText(data, null).Any(char.IsLetter); }

        // Text that belongs to a credit block (copyright, phone numbers, e-mail, web address...) and not to the song.
        // Note: a single "_" is NOT a sign of credits, many files use it as part of real syllables ("_na", "ne_").
        static bool LooksLikeCredit(string t)
        {
            string l = t.ToLowerInvariant().Replace('_', ' ');
            if (CreditMarks.Any(m => l.Contains(m))) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"\b(midi|karaoke)\b|\bfax\s?:")) return true;
            if (System.Text.RegularExpressions.Regex.IsMatch(t.Trim(), @"^\p{Lu}\p{Ll}+_\p{Lu}\p{Ll}+$")) return true;   // a name written "Petar_Benke"
            if (System.Text.RegularExpressions.Regex.IsMatch(l, @"[a-z0-9]{2,}\.(com|net|org|co\.yu|rs|hr|ba|de|si|me)\b")) return true;   // web address
            string tr = t.Trim();
            if (tr.Length >= 8 && tr.Count(c => c == '_') >= 2) return true;                  // "words_joined_like_this"
            if (System.Text.RegularExpressions.Regex.IsMatch(tr, @"^\+\s?\d")) return true;   // "+385..."
            return l.Count(char.IsDigit) >= 7;                                                // phone numbers
        }

        /// <summary>
        /// The words of the song without its credit blocks: blank events at the very start, and any run of blank /
        /// credit-like / number-fragment events that really contains a credit. A file with nothing but credits gives an empty list.
        /// </summary>
        public static List<MidiEvent> WithoutCredits(List<MidiEvent> evs, Encoding enc)
        {
            int n = evs.Count;
            var texts = evs.Select(e => DecodeText(e.Data, enc)).ToList();
            var keep = Enumerable.Repeat(true, n).ToArray();
            int i = 0;
            while (i < n && IsBlankText(texts[i])) keep[i++] = false;
            int j = 0;
            while (j < n)
            {
                if (IsBlankText(texts[j]) || IsNumberBit(texts[j]) || LooksLikeCredit(texts[j]))
                {
                    int k = j; bool credit = false;
                    while (k < n && (IsBlankText(texts[k]) || IsNumberBit(texts[k]) || LooksLikeCredit(texts[k]))) { if (LooksLikeCredit(texts[k])) credit = true; k++; }
                    if (credit) for (int m = j; m < k; m++) keep[m] = false;
                    j = k;
                }
                else j++;
            }
            return evs.Where((e, idx) => keep[idx]).ToList();
        }

        /// <summary>Karaoke text of the file ("" if none). Understands Lyric events and .kar text events.</summary>
        public static string ExtractLyrics(MidiSequence s, Encoding enc = null, bool hideCredits = false)
        {
            var src = LyricEvents(s);
            if (hideCredits) src = WithoutCredits(src, enc);
            var sb = new StringBuilder();
            foreach (var e in src)
            {
                string t = DecodeText(e.Data, enc).Replace("\r\n", "\n").Replace('\r', '\n');
                if (t.StartsWith("\\")) { sb.Append("\n\n"); t = t.Substring(1); }
                else if (t.StartsWith("/")) { sb.Append('\n'); t = t.Substring(1); }
                sb.Append(t);
            }
            return sb.ToString().Trim('\n', ' ').Replace("\n", "\r\n");
        }

        /// <summary>Plays all clips together from the start. Tempo comes from the first clip.</summary>
        public static MidiSequence Overlay(IList<MidiSequence> clips)
        {
            int ppq = clips[0].Ppq;
            var res = new MidiSequence { Ppq = ppq };
            var conductor = new List<MidiEvent>();
            res.Tracks.Add(conductor);
            for (int k = 0; k < clips.Count; k++)
            {
                var clip = Rescale(clips[k], ppq);
                res.Length = Math.Max(res.Length, clip.Length);
                foreach (var t in clip.Tracks)
                {
                    var nt = new List<MidiEvent>();
                    foreach (var e in t)
                    {
                        if (e.IsConductor) { if (k == 0) conductor.Add(e.Clone()); }
                        else nt.Add(e.Clone());
                    }
                    if (nt.Count > 0) res.Tracks.Add(nt);
                }
            }
            return res;
        }
    }
}
