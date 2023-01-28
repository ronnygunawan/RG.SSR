namespace RG.SSR.Options
{
    public class PreactOptions
    {
        public string PreactUmdLibraryResourceName { get; set; } = "preact.umd.min.js";
        public string PreactHooksUmdLibraryResourceName { get; set; } = "preact.hooks.umd.min.js";
        public string PreactCompatUmdLibraryResourceName { get; set; } = "preact.compat.umd.min.js";
        public bool InlineLibrary { get; set; } = true;
        public bool ReactCompat { get; set; } = false;
    }
}
