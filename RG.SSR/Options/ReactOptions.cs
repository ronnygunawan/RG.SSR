namespace RG.SSR.Options
{
    public class ReactOptions
    {
        public string ReactLibraryResourceName { get; set; } = "umd.react.production.min.js";
        public string ReactDomLibraryResourceName { get; set; } = "umd.react-dom.production.min.js";
        public bool InlineLibrary { get; set; } = true;
    }
}
