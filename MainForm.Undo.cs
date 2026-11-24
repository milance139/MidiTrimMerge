using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace MidiTool
{
    // Undo / redo of changes to the song list and the settings.
    public partial class MainForm
    {
        readonly Button btnUndo = new Button();
        readonly Button btnRedo = new Button();
        readonly CheckBox chkFade = new CheckBox();

        int dataVersion;      // bumped on every change; used to know when a prepared song / merge is out of date
        int undoSuppress;     // > 0 while we change things ourselves (restoring, renaming items) so that it is not recorded

        class Snapshot
        {
            public List<Clip> Clips;
            public int Mode, Sel;
            public decimal Overlap;
            public bool Fade;
        }

        readonly List<Snapshot> undoStack = new List<Snapshot>();
        readonly List<Snapshot> redoStack = new List<Snapshot>();
        string lastUndoKind;
        DateTime lastUndoTime;

        Snapshot TakeSnapshot()
        {
            return new Snapshot
            {
                Clips = clips.Select(c => c.Copy()).ToList(),
                Mode = cmbMode.SelectedIndex,
                Overlap = overlap.Value,
                Fade = chkFade.Checked,
                Sel = list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0]
            };
        }

        /// <summary>Call BEFORE changing something. Quick repeated changes of the same kind (spinner clicks) are one undo step.</summary>
        void PushUndo(string kind)
        {
            dataVersion++;
            RequestAutosave();
            if (undoSuppress > 0) return;
            string key = kind + "#" + (list.SelectedIndices.Count == 0 ? -1 : list.SelectedIndices[0]);
            var now = DateTime.UtcNow;
            if (key == lastUndoKind && undoStack.Count > 0 && (now - lastUndoTime).TotalMilliseconds < 1500)
            {
                lastUndoTime = now;
                return;
            }
            lastUndoKind = key;
            lastUndoTime = now;
            undoStack.Add(TakeSnapshot());
            if (undoStack.Count > 100) undoStack.RemoveAt(0);
            redoStack.Clear();
            UpdateUndoButtons();
        }

        void ClearUndo()
        {
            undoStack.Clear();
            redoStack.Clear();
            lastUndoKind = null;
            UpdateUndoButtons();
        }

        void UpdateUndoButtons()
        {
            btnUndo.Enabled = undoStack.Count > 0;
            btnRedo.Enabled = redoStack.Count > 0;
        }

        void RestoreSnapshot(Snapshot s)
        {
            undoSuppress++;
            try
            {
                clips.Clear();
                foreach (var c in s.Clips) clips.Add(c.Copy());   // copy again: the snapshot itself must stay untouched
                cmbMode.SelectedIndex = s.Mode;
                overlap.Value = s.Overlap;
                chkFade.Checked = s.Fade;
                dataVersion++;
                Refresh(Math.Min(s.Sel, clips.Count - 1));
            }
            finally { undoSuppress--; }
            lastUndoKind = null;
            UpdateUndoButtons();
        }

        void Undo()
        {
            if (undoStack.Count == 0) return;
            var target = undoStack[undoStack.Count - 1];
            undoStack.RemoveAt(undoStack.Count - 1);
            redoStack.Add(TakeSnapshot());
            RestoreSnapshot(target);
        }

        void Redo()
        {
            if (redoStack.Count == 0) return;
            var target = redoStack[redoStack.Count - 1];
            redoStack.RemoveAt(redoStack.Count - 1);
            undoStack.Add(TakeSnapshot());
            RestoreSnapshot(target);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // inside a text box Ctrl+Z must keep working as text undo
            bool typing = ActiveControl is TextBoxBase || ActiveControl is NumericUpDown || ActiveControl is ComboBox;
            if (!typing)
            {
                if (keyData == (Keys.Control | Keys.Z)) { Undo(); return true; }
                if (keyData == (Keys.Control | Keys.Y) || keyData == (Keys.Control | Keys.Shift | Keys.Z)) { Redo(); return true; }
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
    }
}
