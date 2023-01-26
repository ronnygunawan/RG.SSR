using Microsoft.ClearScript.V8;

namespace RG.SSR.JavaScript
{
    internal sealed class JavaScriptEngine : IDisposable
    {
        private V8ScriptEngine? _scriptEngine;
        private readonly object _scriptEngineLock = new();
        private bool _disposedValue;

        private V8ScriptEngine GetScriptEngine()
        {
            if (_scriptEngine == null)
            {
                lock (_scriptEngineLock)
                {
                    if (_scriptEngine == null)
                    {
                        _scriptEngine = new V8ScriptEngine();
                    }
                }
            }

            return _scriptEngine;
        }

        public string Render(string script)
        {
            V8ScriptEngine scriptEngine = GetScriptEngine();

            if (scriptEngine.Evaluate(script) is not string result)
            {
                throw new InvalidOperationException("The script did not return a string.");
            }

            return result;
        }

        private void Dispose(bool disposing)
        {
            if (_disposedValue)
            {
                return;
            }
            
            if (disposing)
            {
                // dispose managed state (managed objects)
                _scriptEngine?.Dispose();
            }

            _disposedValue = true;
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
