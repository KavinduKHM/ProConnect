namespace ProConnect.WebAPI
{
    public static class RateLimitPolicies
    {
        /// <summary>Per-user cap on the endpoints that spend Gemini quota.</summary>
        public const string Ai = "ai";
    }
}
