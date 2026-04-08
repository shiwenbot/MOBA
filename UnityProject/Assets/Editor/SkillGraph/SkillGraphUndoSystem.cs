using System;
using System.Collections.Generic;

namespace TEngine.Editor.SkillGraph
{
    internal sealed class SkillGraphUndoSystem
    {
        private readonly List<string> _undoTimeline = new List<string>();
        private readonly Stack<string> _redoStack = new Stack<string>();
        private readonly int _maxHistoryDepth;

        public SkillGraphUndoSystem(int maxHistoryDepth = 50)
        {
            _maxHistoryDepth = Math.Max(2, maxHistoryDepth);
        }

        public bool CanUndo => _undoTimeline.Count > 1;

        public bool CanRedo => _redoStack.Count > 0;

        public void Clear()
        {
            _undoTimeline.Clear();
            _redoStack.Clear();
        }

        public void Record(string snapshotJson)
        {
            string normalizedSnapshot = snapshotJson ?? string.Empty;
            if (_undoTimeline.Count > 0 && string.Equals(_undoTimeline[_undoTimeline.Count - 1], normalizedSnapshot, StringComparison.Ordinal))
                return;

            _undoTimeline.Add(normalizedSnapshot);
            TrimUndoTimeline();
            _redoStack.Clear();
        }

        public bool TryUndo(out string snapshotJson)
        {
            snapshotJson = string.Empty;
            if (!CanUndo)
                return false;

            int currentIndex = _undoTimeline.Count - 1;
            string currentSnapshot = _undoTimeline[currentIndex];
            _undoTimeline.RemoveAt(currentIndex);
            _redoStack.Push(currentSnapshot);

            snapshotJson = _undoTimeline[_undoTimeline.Count - 1];
            return true;
        }

        public bool TryRedo(out string snapshotJson)
        {
            snapshotJson = string.Empty;
            if (!CanRedo)
                return false;

            string redoSnapshot = _redoStack.Pop();
            _undoTimeline.Add(redoSnapshot);
            TrimUndoTimeline();
            snapshotJson = redoSnapshot;
            return true;
        }

        private void TrimUndoTimeline()
        {
            while (_undoTimeline.Count > _maxHistoryDepth)
                _undoTimeline.RemoveAt(0);
        }
    }
}
