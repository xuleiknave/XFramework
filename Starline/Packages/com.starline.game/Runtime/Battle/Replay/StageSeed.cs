namespace Starline.Battle
{
    /// <summary>
    /// Stage battles use a fixed seed derived from the stage id (replay-server.md section 4):
    /// everyone faces the same rolls, so a copied formation plays out the same, and nobody can
    /// retry seeds until a weak formation gets lucky.
    /// </summary>
    public static class StageSeed
    {
        public static ulong For(string stageId)
        {
            ulong seed = Fnv.Hash("stage:" + stageId);
            return seed == 0 ? 1 : seed;
        }
    }
}
