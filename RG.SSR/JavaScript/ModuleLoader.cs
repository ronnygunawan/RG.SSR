using Microsoft.ClearScript;
using System.Collections.Concurrent;

namespace RG.SSR.JavaScript
{
    internal sealed class ModuleLoader : DocumentLoader
    {
        private readonly ConcurrentDictionary<string, Document> _moduleByName = new();

        public string GetOrAddModule(string name, Func<(string Code, DocumentCategory Category)> valueFactory)
        {
            Document document = _moduleByName.GetOrAdd(
                key: name,
                valueFactory: _ =>
                {
                    (string code, DocumentCategory category) = valueFactory.Invoke();

                    return new StringDocument(
                        info: new DocumentInfo(name)
                        {
                            Category = category
                        },
                        contents: code
                    );
                }
            );

            using StreamReader reader = new(document.Contents);
            return reader.ReadToEnd();
        }

        public override Task<Document> LoadDocumentAsync(
            DocumentSettings settings,
            DocumentInfo? sourceInfo,
            string specifier,
            DocumentCategory category,
            DocumentContextCallback contextCallback
        )
        {
            if (_moduleByName.TryGetValue(
                key: specifier,
                out Document? module
            ))
            {
                return Task.FromResult(module);
            }

            return Default.LoadDocumentAsync(
                settings,
                sourceInfo,
                specifier,
                category,
                contextCallback
            );
        }
    }
}
