using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Threading;

namespace Simulator
{
    public class ConcurrentStreamWriter : IDisposable
    {
        private readonly TextWriter _textWriter;
        private readonly BlockingCollection<string> _writeQueue;
        private Task _backgroundWriterTask;

        public ConcurrentStreamWriter(TextWriter textWriter)
        {
            _textWriter = textWriter;
            _writeQueue = new BlockingCollection<string>();
            _backgroundWriterTask = Task.Run(() => BackgroundWritingLoop());            
        }

        public ConcurrentStreamWriter(string path, FileMode fileMode = FileMode.CreateNew,
            FileAccess fileAccess = FileAccess.Write)
            : this(new StreamWriter(new FileStream(path, fileMode, fileAccess)))
        {
        }

        public void Write(string stringToWrite)
        {
            _writeQueue.Add(stringToWrite);
        }

        public void WriteLine(string stringToWrite)
        {
            _writeQueue.Add(stringToWrite + "\r\n");
        }

        private bool _closed = false;
        public void Close()
        {
            bool close = false;
            lock (this)
            {
                if (!_closed)
                {
                    _closed = close = true;
                }
            }

            if (close)
            {
                _writeQueue.CompleteAdding();
                _backgroundWriterTask.Wait();
                _textWriter.Flush();
                _textWriter.Dispose();
            }
        }
        public void Dispose()
        {
            Close();
        }


        private void BackgroundWritingLoop()
        {
            try
            {
                foreach (string item in _writeQueue.GetConsumingEnumerable())
                {
                    _textWriter.Write(item);
                }
            }
            catch (System.OperationCanceledException)
            {}
        }

    }
}
