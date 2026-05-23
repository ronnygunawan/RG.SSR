using FsCheck;
using FsCheck.Xunit;
using RG.SSR.JavaScript;
using SystemRandom = System.Random;

namespace RG.SSR.Tests.Properties;

// Feature: es-module-support, Property 2: Module Syntax Detection Accuracy
// Validates: Requirements 6.1, 6.5

/// <summary>
/// Property-based tests verifying that ModuleSyntaxDetector.ContainsModuleSyntax
/// returns true only when import/export keywords appear as actual JavaScript statements,
/// not when they appear inside comments or string literals.
/// </summary>
public class ModuleSyntaxDetectorProperties
{
    /// <summary>
    /// Generates a random valid JavaScript identifier (variable/function name).
    /// </summary>
    private static Gen<string> GenIdentifier()
    {
        return Gen.Elements("foo", "bar", "baz", "myVar", "utils", "helper", "Component", "render", "data");
    }

    /// <summary>
    /// Generates a random string that does NOT contain import/export keywords.
    /// Used as filler content in generated sources.
    /// </summary>
    private static Gen<string> GenSafeContent()
    {
        return Gen.Elements(
            "var x = 1;",
            "function hello() { return 42; }",
            "const y = 'hello world';",
            "let z = true;",
            "console.log('test');",
            "if (a > b) { c = d; }",
            "for (var i = 0; i < 10; i++) {}",
            "var result = a + b;",
            "// this is a regular comment",
            ""
        );
    }

    /// <summary>
    /// Generates actual import statements that should be detected.
    /// </summary>
    private static Gen<string> GenActualImportStatement()
    {
        return Gen.OneOf(
            GenIdentifier().Select(id => $"import {{ {id} }} from './module.js';"),
            GenIdentifier().Select(id => $"import {id} from 'some-module';"),
            Gen.Constant("import 'side-effect-module';"),
            GenIdentifier().Select(id => $"import * as {id} from './utils.js';"),
            GenIdentifier().Select(id => $"import {id}, {{ named }} from './lib.js';")
        );
    }

    /// <summary>
    /// Generates actual export statements that should be detected.
    /// </summary>
    private static Gen<string> GenActualExportStatement()
    {
        return Gen.OneOf(
            GenIdentifier().Select(id => $"export default function {id}() {{ return null; }}"),
            GenIdentifier().Select(id => $"export function {id}() {{ return 42; }}"),
            GenIdentifier().Select(id => $"export const {id} = 'value';"),
            GenIdentifier().Select(id => $"export let {id} = true;"),
            GenIdentifier().Select(id => $"export var {id} = 123;"),
            GenIdentifier().Select(id => $"export class {id} {{}}"),
            GenIdentifier().Select(id => $"export {{ {id} }};"),
            Gen.Constant("export * from './module.js';"),
            GenIdentifier().Select(id => $"export * as {id} from './module.js';")
        );
    }

    /// <summary>
    /// Generates import/export keywords hidden inside single-line comments.
    /// </summary>
    private static Gen<string> GenImportExportInSingleLineComment()
    {
        return Gen.OneOf(
            GenIdentifier().Select(id => $"// import {{ {id} }} from './module.js';"),
            GenIdentifier().Select(id => $"// export default function {id}() {{}}"),
            Gen.Constant("// import 'something';"),
            Gen.Constant("// export const x = 1;"),
            GenIdentifier().Select(id => $"// export function {id}() {{}}")
        );
    }

    /// <summary>
    /// Generates import/export keywords hidden inside multi-line comments.
    /// </summary>
    private static Gen<string> GenImportExportInMultiLineComment()
    {
        return Gen.OneOf(
            GenIdentifier().Select(id => $"/* import {{ {id} }} from './module.js'; */"),
            GenIdentifier().Select(id => $"/* export default function {id}() {{}} */"),
            Gen.Constant("/* import 'something'; */"),
            Gen.Constant("/* export const x = 1; */"),
            Gen.Constant("/*\nimport foo from 'bar';\nexport default foo;\n*/")
        );
    }

    /// <summary>
    /// Generates import/export keywords hidden inside string literals.
    /// </summary>
    private static Gen<string> GenImportExportInString()
    {
        return Gen.OneOf(
            Gen.Constant("var s = \"import foo from 'bar'\";"),
            Gen.Constant("var s = 'export default function() {}';"),
            Gen.Constant("var s = `import { x } from './y.js'`;"),
            Gen.Constant("var s = \"export const z = 1\";"),
            Gen.Constant("var s = 'import * as ns from \"mod\"';"),
            Gen.Constant("var s = `export function hello() {}`")
        );
    }

    /// <summary>
    /// Generates a source that contains ONLY import/export inside comments and strings (no actual statements).
    /// Should return false from ContainsModuleSyntax.
    /// </summary>
    private static Gen<string> GenSourceWithNoActualModuleSyntax()
    {
        var hiddenLine = Gen.OneOf(
            GenImportExportInSingleLineComment(),
            GenImportExportInMultiLineComment(),
            GenImportExportInString()
        );

        return Gen.NonEmptyListOf(Gen.OneOf(hiddenLine, GenSafeContent()))
            .Select(lines => string.Join("\n", lines));
    }

    /// <summary>
    /// Generates a source that contains at least one actual import or export statement.
    /// Should return true from ContainsModuleSyntax.
    /// </summary>
    private static Gen<string> GenSourceWithActualModuleSyntax()
    {
        var actualStatement = Gen.OneOf(GenActualImportStatement(), GenActualExportStatement());
        var otherLine = Gen.OneOf(
            GenSafeContent(),
            GenImportExportInSingleLineComment(),
            GenImportExportInMultiLineComment(),
            GenImportExportInString()
        );

        return actualStatement.SelectMany(statement =>
            Gen.ListOf(otherLine).Select(otherLines =>
            {
                var allLines = new List<string>(otherLines) { statement };
                // Shuffle to place the actual statement at a random position
                var rng = new SystemRandom(allLines.GetHashCode());
                for (int i = allLines.Count - 1; i > 0; i--)
                {
                    int j = rng.Next(i + 1);
                    (allLines[i], allLines[j]) = (allLines[j], allLines[i]);
                }
                return string.Join("\n", allLines);
            })
        );
    }

    /// <summary>
    /// Property: Sources with actual import/export statements return true.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ActualModuleSyntax_IsDetected()
    {
        return Prop.ForAll(
            GenSourceWithActualModuleSyntax().ToArbitrary(),
            source => ModuleSyntaxDetector.ContainsModuleSyntax(source)
        );
    }

    /// <summary>
    /// Property: Sources with import/export only in comments and strings return false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property HiddenModuleSyntax_IsNotDetected()
    {
        return Prop.ForAll(
            GenSourceWithNoActualModuleSyntax().ToArbitrary(),
            source => !ModuleSyntaxDetector.ContainsModuleSyntax(source)
        );
    }

    /// <summary>
    /// Property: Empty or null sources return false.
    /// </summary>
    [Property(MaxTest = 10)]
    public Property EmptyOrNull_ReturnsFalse()
    {
        return Prop.ForAll(
            Gen.Elements<string?>(null, "", "   ", "\n", "\t").ToArbitrary(),
            source => !ModuleSyntaxDetector.ContainsModuleSyntax(source!)
        );
    }

    /// <summary>
    /// Property: Sources with only safe content (no import/export anywhere) return false.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property SafeContent_ReturnsFalse()
    {
        return Prop.ForAll(
            Gen.NonEmptyListOf(GenSafeContent())
                .Select(lines => string.Join("\n", lines))
                .ToArbitrary(),
            source => !ModuleSyntaxDetector.ContainsModuleSyntax(source)
        );
    }
}
