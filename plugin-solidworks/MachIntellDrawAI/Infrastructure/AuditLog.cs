using System;
using System.IO;

namespace MachIntellDrawAI.Infrastructure
{
    internal sealed class AuditLog
    {
        private readonly string _path;
        private readonly object _gate = new object();

        public AuditLog()
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MachIntell", "DrawingAddin", "logs");
            Directory.CreateDirectory(directory);
            _path = Path.Combine(directory, "drawing-addin.log");
        }

        public void Info(string eventName, string message) => Write("INFO", eventName, message);
        public void Error(string eventName, Exception exception) => Write("ERROR", eventName, exception.GetType().Name + ": " + exception.Message);

        private void Write(string level, string eventName, string message)
        {
            var safe = message.Replace("\r", " ").Replace("\n", " ");
            lock (_gate)
                File.AppendAllText(_path, $"{DateTimeOffset.UtcNow:O}\t{level}\t{eventName}\t{safe}{Environment.NewLine}");
        }
    }
}
