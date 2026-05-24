using RG.SSR.JavaScript;
using Shouldly;
using Xunit;

namespace RG.SSR.Tests.Unit;

public class ModuleLoaderTests
{
    private readonly ModuleLoader _loader = new();

    [Fact]
    public void RegisterModule_NullSpecifier_ThrowsArgumentException()
    {
        var ex = Should.Throw<ArgumentException>(() => _loader.RegisterModule(null!, "export default 42;"));
        ex.ParamName.ShouldBe("specifier");
    }

    [Fact]
    public void RegisterModule_EmptySpecifier_ThrowsArgumentException()
    {
        var ex = Should.Throw<ArgumentException>(() => _loader.RegisterModule("", "export default 42;"));
        ex.ParamName.ShouldBe("specifier");
    }

    [Fact]
    public void RegisterModule_SpecifierExceeding256Characters_ThrowsArgumentException()
    {
        string longSpecifier = new('a', 257);

        var ex = Should.Throw<ArgumentException>(() => _loader.RegisterModule(longSpecifier, "export default 42;"));
        ex.ParamName.ShouldBe("specifier");
    }

    [Fact]
    public void RegisterModule_NullSourceCode_ThrowsArgumentException()
    {
        var ex = Should.Throw<ArgumentException>(() => _loader.RegisterModule("my-module", null!));
        ex.ParamName.ShouldBe("sourceCode");
    }

    [Fact]
    public void RegisterModule_EmptySourceCode_ThrowsArgumentException()
    {
        var ex = Should.Throw<ArgumentException>(() => _loader.RegisterModule("my-module", ""));
        ex.ParamName.ShouldBe("sourceCode");
    }

    [Fact]
    public void RegisterModule_DuplicateRegistration_IsSilentlyIgnored()
    {
        _loader.RegisterModule("utils", "export const x = 1;");

        // Should not throw - duplicate is silently ignored
        _loader.RegisterModule("utils", "export const x = 2;");
    }
}
