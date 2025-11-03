using System.Text;
using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire.K6;

public class K6ScriptResource : ExecutableResource, IResourceWithParent<K6ServerResource>
{
    private readonly Context _context;

    public class Context
    {
        public string Name { get; }
        public string Path { get; }

        public string ParameterName => $"script-{Name}";
        
        public Context(string directory, FileInfo fileInfo)
        {
            if (fileInfo.Extension != ".js")
            {
                throw new InvalidOperationException($"The file {fileInfo} is not JS Script");
            }
            Name = fileInfo.Name.Replace(".js", "").Replace("_", "-").Replace(".", "-");
            Path = fileInfo.Name;
        }
    }

    public bool Initialized { get; set; }
    
    public ReferenceExpression ScriptFileReference => ReferenceExpression.Create($"{ScriptFileParameter}");
    
    public ParameterResource ScriptFileParameter { get; set; }

    /// <inheritdoc />
    public K6ServerResource Parent { get; }
    
    public string ScriptArg { get; }
    public string ScriptName => _context.Name;
    
    /// <inheritdoc />
    public K6ScriptResource(Context context, K6ServerResource parent, ParameterResource scriptFileName) 
        : base(context.Name, "aspire", parent.ScriptDirectory)
    {
        _context = context;
        Parent = parent;
        ScriptFileParameter = scriptFileName;

        ScriptArg = new StringBuilder(parent.ScriptDirectory.StartsWith('.')
                ? parent.ScriptDirectory.Remove(0, 1)
                : parent.ScriptDirectory)
            .Append('/')
            .Append(context.Path)
            .ToString();
    }
}