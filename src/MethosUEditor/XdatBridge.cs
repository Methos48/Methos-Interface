using Python.Runtime;

namespace MethosUEditor;

internal static class XdatBridge
{
    private static readonly object Sync = new();
    private static bool initialized;

    private static void EnsureInitialized()
    {
        lock (Sync)
        {
            if (initialized) return;
            string runtimeRoot = Path.Combine(AppContext.BaseDirectory, "XdatRuntime");
            string pythonRoot = Path.Combine(runtimeRoot, "Python");
            string pythonDll = Path.Combine(pythonRoot, "python312.dll");
            string stdlibZip = Path.Combine(pythonRoot, "python312.zip");
            string corePath = Path.Combine(runtimeRoot, "xdat_core.py");
            string bridgePath = Path.Combine(runtimeRoot, "xdat_bridge.py");
            if (!File.Exists(pythonDll) || !File.Exists(stdlibZip) ||
                !File.Exists(corePath) || !File.Exists(bridgePath))
                throw new FileNotFoundException("Falta el runtime interno de XDAT. Reinstala Methos para reparar sus componentes.");

            Runtime.PythonDLL = pythonDll;
            PythonEngine.PythonHome = pythonRoot;
            PythonEngine.PythonPath = string.Join(Path.PathSeparator,
                stdlibZip, pythonRoot, runtimeRoot);
            PythonEngine.Initialize();
            initialized = true;
        }
    }

    private static string Invoke(string method, params string[] arguments)
    {
        EnsureInitialized();
        using (Py.GIL())
        using (PyObject module = Py.Import("xdat_bridge"))
        {
            var args = arguments.Select(value => (PyObject)new PyString(value)).ToArray();
            try
            {
                using PyObject result = module.InvokeMethod(method, args);
                return result.As<string>();
            }
            finally
            {
                foreach (PyObject argument in args) argument.Dispose();
            }
        }
    }

    public static (int Windows, int Shortcuts) Decompile(string sourcePath, string partsPath)
    {
        string[] result = Invoke("decompile_file", sourcePath, partsPath).Split('|');
        if (result.Length != 2) throw new InvalidDataException("Respuesta inválida del motor XDAT.");
        return (int.Parse(result[0]), int.Parse(result[1]));
    }

    public static (int Windows, int Shortcuts, long Bytes) Compile(
        string partsPath, string originalPath, string temporaryOutputPath)
    {
        string[] result = Invoke("compile_file", partsPath, originalPath, temporaryOutputPath).Split('|');
        if (result.Length != 3) throw new InvalidDataException("Respuesta inválida del motor XDAT.");
        return (int.Parse(result[0]), int.Parse(result[1]), long.Parse(result[2]));
    }
}
