using System;

namespace SimpleTarkovManager.Models
{
    /// <summary>
    /// Represents an error that occurs during the file patching process.
    /// It contains specific details about the file that failed.
    /// </summary>
    public class PatchingException : Exception
    {
        public string FailedFilePath { get; }

        public PatchingException(string message, string failedFilePath, Exception innerException)
            : base(message, innerException)
        {
            FailedFilePath = failedFilePath;
        }
    }
}