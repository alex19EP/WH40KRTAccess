namespace RTAccess.UI
{
    /// <summary>
    /// Cached lookup of a live game VIEW component. Most screens read the VM and never need this, but some
    /// state lives only on the view — text a coroutine/callback wrote straight into a label (the main
    /// menu's message of the day), or "is this popup still showing" where the VM outlives the window (the
    /// Dark Heresy promo, which the view hides without disposing its VM).
    ///
    /// A scene search is not free, so the found component is cached per type and re-resolved only when the
    /// cached one has been destroyed (Unity's null-overload catches that) — a new scene builds new views.
    /// Inactive objects are included: a hidden popup's view is exactly the case we need to inspect.
    /// </summary>
    internal static class LiveView
    {
        private static readonly Dictionary<Type, UnityEngine.Object> Cache = new Dictionary<Type, UnityEngine.Object>();

        public static T Find<T>() where T : UnityEngine.Component
        {
            UnityEngine.Object cached;
            if (Cache.TryGetValue(typeof(T), out cached) && cached != null) return (T)cached;
            var found = UnityEngine.Object.FindAnyObjectByType<T>(UnityEngine.FindObjectsInactive.Include);
            if (found != null) Cache[typeof(T)] = found;
            else Cache.Remove(typeof(T));
            return found;
        }
    }
}
