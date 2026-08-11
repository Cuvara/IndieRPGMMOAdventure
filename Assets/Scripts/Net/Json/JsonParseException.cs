using System;

namespace Scripts.Net.Json
{
    /// <summary>Thrown when a frame body is not well-formed JSON.</summary>
    public sealed class JsonParseException : Exception
    {
        public JsonParseException(string message)
            : base(message)
        {
        }
    }
}
