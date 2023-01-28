namespace RG.SSR.Options
{
    public class ServerSideRendererOptions
    {
        public ReactOptions React { get; } = new();
        public PreactOptions Preact { get; } = new();
    }
}
