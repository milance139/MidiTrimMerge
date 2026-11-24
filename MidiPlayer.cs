using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace MidiTool
{
    /// <summary>Plays a MidiSequence through a Windows MIDI output (the built-in synthesizer by default).</summary>
    class MidiPlayer
    {
        [DllImport("winmm.dll")] static extern int midiOutOpen(out IntPtr h, uint device, IntPtr cb, IntPtr inst, uint flags);
        [DllImport("winmm.dll")] static extern int midiOutClose(IntPtr h);
        [DllImport("winmm.dll")] static extern int midiOutShortMsg(IntPtr h, uint msg);
        [DllImport("winmm.dll")] static extern int midiOutReset(IntPtr h);
        [DllImport("winmm.dll")] static extern uint midiOutGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Auto)] static extern int midiOutGetDevCaps(UIntPtr id, ref MIDIOUTCAPS caps, uint size);
        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct MIDIOUTCAPS
        {
            public ushort wMid, wPid;
            public uint vDriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szPname;
            public ushort wTechnology, wVoices, wNotes, wChannelMask;
            public uint dwSupport;
        }

        /// <summary>Names of the MIDI output devices installed on this computer (index = device number).</summary>
        public static List<string> GetDevices()
        {
            var l = new List<string>();
            try
            {
                uint n = midiOutGetNumDevs();
                for (uint i = 0; i < n; i++)
                {
                    var caps = new MIDIOUTCAPS();
                    if (midiOutGetDevCaps((UIntPtr)i, ref caps, (uint)Marshal.SizeOf(typeof(MIDIOUTCAPS))) == 0) l.Add(caps.szPname);
                    else l.Add("MIDI " + i);
                }
            }
            catch { }
            return l;
        }

        struct Msg { public double Sec; public uint Data; }

        Thread thread;
        volatile bool stopRequested;
        Stopwatch clock = new Stopwatch();
        double startOffset;

        /// <summary>Raised on the playback thread when playback ends or is stopped.</summary>
        public event Action Finished;

        public bool IsPlaying { get { var t = thread; return t != null && t.IsAlive; } }

        /// <summary>Where playback is now, in seconds from the start of the sequence.</summary>
        public double Position { get { return startOffset + clock.Elapsed.TotalSeconds; } }

        /// <summary>Starts playing from startSec. device: -1 = Windows default (MIDI mapper), otherwise the device number.</summary>
        public void Start(MidiSequence seq, double startSec = 0, int device = -1)
        {
            Stop();
            var tm = new TempoMap(seq);
            var all = new List<Msg>();
            foreach (var e in MidiOps.OrderForPlayback(seq.Tracks.SelectMany(t => t).Where(e => e.IsChannel)))
            {
                bool oneByte = e.Type == 0xC0 || e.Type == 0xD0;
                all.Add(new Msg { Sec = tm.TickToSec(e.Tick), Data = (uint)(e.Status | (e.D1 << 8) | (oneByte ? 0 : (e.D2 << 16))) });
            }
            double total = tm.TickToSec(seq.Length);
            if (startSec < 0) startSec = 0;

            // when starting in the middle: programs, volumes, pitch bend... before that point are still applied, notes are not
            var setup = new List<Msg>();
            var msgs = new List<Msg>();
            foreach (var m in all)
            {
                int type = (int)(m.Data & 0xF0);
                if (m.Sec < startSec) { if (type != 0x80 && type != 0x90) setup.Add(m); }
                else msgs.Add(m);
            }

            IntPtr h;
            uint dev = device < 0 ? 0xFFFFFFFF : (uint)device;   // 0xFFFFFFFF = MIDI_MAPPER
            int r = midiOutOpen(out h, dev, IntPtr.Zero, IntPtr.Zero, 0);
            if (r != 0 && device >= 0) r = midiOutOpen(out h, 0xFFFFFFFF, IntPtr.Zero, IntPtr.Zero, 0);   // chosen device unavailable: fall back
            if (r != 0) throw new InvalidOperationException("Could not open the MIDI output device (code " + r + ").");

            stopRequested = false;
            startOffset = startSec;
            clock.Reset();
            thread = new Thread(() => Run(h, setup, msgs, total, startSec)) { IsBackground = true };
            thread.Start();
        }

        public void Stop()
        {
            stopRequested = true;
            var t = thread;
            if (t != null && t.IsAlive && t != Thread.CurrentThread) t.Join(1500);
        }

        void Run(IntPtr h, List<Msg> setup, List<Msg> msgs, double total, double startSec)
        {
            timeBeginPeriod(1);
            try
            {
                foreach (var m in setup) midiOutShortMsg(h, m.Data);
                clock.Restart();
                foreach (var m in msgs)
                {
                    if (!WaitUntil(m.Sec - startSec)) break;
                    midiOutShortMsg(h, m.Data);
                }
                if (!stopRequested) WaitUntil(total - startSec + 0.3);
            }
            catch (Exception) { }   // playback just ends; an error here must never take the program down
            finally
            {
                clock.Stop();      // freezes Position
                midiOutReset(h);   // silences any note that is still sounding
                midiOutClose(h);
                timeEndPeriod(1);
                var f = Finished;
                if (f != null) f();
            }
        }

        bool WaitUntil(double sec)
        {
            while (true)
            {
                if (stopRequested) return false;
                double rem = sec - clock.Elapsed.TotalSeconds;
                if (rem <= 0) return true;
                Thread.Sleep(rem > 0.005 ? 1 : 0);
            }
        }
    }
}
