namespace Session.Core.Content
{
    /// <summary>
    /// Turns an authoring string into the stable int key Core passes around.
    ///
    /// Core deals in ints so nothing on a per-frame path touches a string. Designers deal in names
    /// like "prop.bottle.label" because nobody can author against integers. This is the seam.
    ///
    /// FNV-1a, 32-bit. The hash is part of the save/wire format the moment content ships — do not
    /// change the algorithm without a migration.
    /// </summary>
    public static class ContentKey
    {
        private const uint OffsetBasis = 2166136261u;
        private const uint Prime = 16777619u;

        public const int None = 0;

        public static int Of(string? value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return None;
            }

            uint hash = OffsetBasis;
            for (int i = 0; i < value!.Length; i++)
            {
                hash ^= value[i];
                hash *= Prime;
            }

            // Fold away the sign and reserve 0 for "no key", so None is unambiguous.
            int key = (int)(hash & 0x7FFFFFFFu);
            return key == None ? 1 : key;
        }
    }
}
