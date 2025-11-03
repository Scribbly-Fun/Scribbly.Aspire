using System.Text;
using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire.K6;

public class K6ScriptResource : ExecutableResource, IResourceWithParent<K6ServerResource>
{
    public bool Initialized { get; set; }
    public string ScriptArg { get; }
    public int VirtualUsers { get; set; } = 10;
    public int Duration { get; set; } = 30;
    
    /// <inheritdoc />
    public K6ServerResource Parent { get; }
    
    public ReferenceExpression ScriptFileReference => ReferenceExpression.Create($"{ScriptArg}");
    public ReferenceExpression VirtualUsersReference => ReferenceExpression.Create($"{VirtualUsers.ToString()}");
    public ReferenceExpression DurationReference => ReferenceExpression.Create($"{Duration.ToString()}s");
    
    private K6ScriptResource(string name, string file, K6ServerResource parent)
        : base(name, "echo", Directory.GetCurrentDirectory())
    {
        Parent = parent;
        
        ScriptArg = new StringBuilder(parent.ScriptDirectory.StartsWith('.')
                ? parent.ScriptDirectory.Remove(0, 1)
                : parent.ScriptDirectory)
            .Append('/')
            .Append(file)
            .ToString();
    }

    public static K6ScriptResource CreateScriptResource(FileInfo scriptInfo, K6ServerResource serviceResource)
    {
        var name = scriptInfo.Name.Replace(".js", "").Replace("_", "-").Replace(".", "-");
        var path = scriptInfo.Name;
        
        return new K6ScriptResource(name, path, serviceResource);
    }
}