#if CUVARA_DOTS
namespace Scripts.DI.Dots
{
    /// <summary>
    /// A view asset provider that keeps one lease per key and can say who claimed a key last.
    /// </summary>
    /// <remarks>
    /// The epoch of a key rises every time a caller starts a prewarm or an async acquire for it.
    /// A scene installation records the epoch when it prewarms a key; a late cleanup of that
    /// prewarm releases the key only while the epoch is unchanged, so it never drops the lease a
    /// newer installation took over. Providers without this interface keep the plain behaviour.
    /// </remarks>
    public interface IViewClaimEpochs
    {
        /// <summary>The key's current claim epoch; 0 for a key never claimed.</summary>
        int ClaimEpoch(string key);
    }
}
#endif
