namespace Starline.Server.Data
{
    /// <summary>A one-time link code (ServerOps.CreateLinkCode): the digits are the id.</summary>
    public sealed class LinkCodeRecord
    {
        public string Code;
        public string AccountId;
        public long ExpiresAt;

        public LinkCodeRecord Clone() => (LinkCodeRecord)MemberwiseClone();
    }
}
