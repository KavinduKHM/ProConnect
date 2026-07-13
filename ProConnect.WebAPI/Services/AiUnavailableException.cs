using System;

namespace ProConnect.WebAPI.Services
{
    /// <summary>
    /// Raised when the AI provider cannot fulfil the request. The message is safe to show to the user.
    /// </summary>
    public class AiUnavailableException : Exception
    {
        public AiUnavailableException(string message) : base(message) { }
    }
}
