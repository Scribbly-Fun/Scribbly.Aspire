using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire.K6;

public class K6ScriptResource : Resource, IResourceWithParent<K6ServerResource>
{
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
    
    public ReferenceExpression ScriptFileReference => ReferenceExpression.Create($"{ScriptFileParameter}");
    
    public ParameterResource ScriptFileParameter { get; set; }

    /// <inheritdoc />
    public K6ServerResource Parent { get; }
    
    /// <inheritdoc />
    public K6ScriptResource(Context context, K6ServerResource parent, ParameterResource scriptFileName) : base(context.Name)
    {
        Parent = parent;
        ScriptFileParameter = scriptFileName;
    }
}