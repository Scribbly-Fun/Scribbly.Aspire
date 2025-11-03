namespace Scribbly.Aspire;

internal sealed class ScriptExecutionContext
{
    internal string? ScriptPath
    {
        get
        {
            if (string.IsNullOrEmpty(_script))
            {
                return null;
            }

            return _directory + "/" + _script;
        }
        set => _script = value;
    }

    internal string? ScriptName
    {
        get
        {
            if (string.IsNullOrEmpty(ScriptPath))
            {
                return null;
            }

            var info = new FileInfo(ScriptPath);
            return info.Name.Replace(info.Extension, "");
        }
    }

    private readonly string _directory;

    private string? _script;

    public ScriptExecutionContext(string directory)
    {
        _directory = directory.StartsWith('.') ? directory.Remove(0, 1) : directory;
    }
}