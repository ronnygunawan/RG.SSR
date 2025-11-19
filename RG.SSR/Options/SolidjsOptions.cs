namespace RG.SSR.Options
{
    public class SolidjsOptions
    {
        public string SolidjsLibraryResourceName { get; set; } = "solid.min.js";
        public string SolidjsWebLibraryResourceName { get; set; } = "web.min.js";
        public bool InlineLibrary { get; set; } = true;
    }
}
