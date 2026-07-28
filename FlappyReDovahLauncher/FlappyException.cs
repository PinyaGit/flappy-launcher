using System;
using System.IO;

namespace FlappyReDovahLauncher
{
    /// <summary>User-facing error with optional technical detail for logs.</summary>
    internal sealed class FlappyException : Exception
    {
        public string UserMessage { get; private set; }

        public FlappyException(string userMessage, string technical = null, Exception inner = null)
            : base(technical ?? userMessage, inner)
        {
            UserMessage = userMessage ?? "Unknown error";
        }

        public static string FormatForUser(Exception ex)
        {
            if (ex is FlappyException fe)
                return fe.UserMessage;
            if (ex is OperationCanceledException)
                return "Operation cancelled.";
            if (ex is UnauthorizedAccessException)
                return "Access denied. Close the game / MO2 and try again, or run as administrator once.";
            if (ex is IOException)
                return "File or disk error:\n" + ex.Message + "\n\nCheck free space and that no other program locks the files.";
            if (ex is System.Net.WebException || ex is System.Net.Http.HttpRequestException)
                return "Network error:\n" + ex.Message + "\n\nCheck internet connection and CDN (cdn.flappy.su).";
            return ex.Message;
        }
    }
}
