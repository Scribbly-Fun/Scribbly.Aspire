using System.Text;
using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire.K6;

public class K6ScriptResource : ExecutableResource, IResourceWithParent<LoadTesterResource>
{
    private bool _initialized;

    internal bool HasResourceBeenInitialized()
    {
        if (!_initialized)
        {
            _initialized = true;
            return false;
        }

        return _initialized;
    }
    
    public string ScriptArg { get; }
    public int VirtualUsers { get; set; } = 10;
    public int Duration { get; set; } = 30;
    
    /// <inheritdoc />
    public LoadTesterResource Parent { get; }
    
    public K6ServerResource Server { get; }
    
    private K6ScriptResource(string name, string file, string command, LoadTesterResource parent)
        : base(name, command, parent.Directory)
    {
        ArgumentNullException.ThrowIfNull(parent.Server);
        
        Parent = parent;
        Server = parent.Server;
        
        ScriptArg = new StringBuilder("/scripts")
            .Append('/')
            .Append(file)
            .ToString();
    }

    public static K6ScriptResource CreateScriptResource(FileInfo scriptInfo, LoadTesterResource parent)
    {
        var name = scriptInfo.Name.Replace(".js", "").Replace("_", "-").Replace(".", "-");
        var path = scriptInfo.Name;
        
        return new K6ScriptResource(name, path, "powershell", parent);
    }
}