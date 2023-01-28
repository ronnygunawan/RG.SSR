using System.Reflection;

namespace RG.SSR
{
    public interface IPreactRenderer
    {
        string Render(Assembly componentAssembly, string componentName, bool isStatic);
        string Render<TProps>(Assembly componentAssembly, string componentName, TProps props, bool isStatic);
    }
}
