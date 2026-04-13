using System;
using System.Collections.Generic;

namespace GameShared.FrameSync.Command
{
    public sealed class CommandPool<T> where T : class, IResettable, new()
    {
        private readonly Stack<T> _pool = new Stack<T>();

        public int AvailableCount => _pool.Count;

        public T Rent()
        {
            return _pool.Count > 0 ? _pool.Pop() : new T();
        }

        public void Return(T command)
        {
            if (command == null)
            {
                throw new ArgumentNullException(nameof(command));
            }

            command.Reset();
            _pool.Push(command);
        }

        public void Clear()
        {
            _pool.Clear();
        }
    }
}
