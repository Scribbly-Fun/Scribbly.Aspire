using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire;

public class K6ScriptResource : Resource
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
            Path = directory + "/" + fileInfo.Name;
        }
    }
    
    public string ContainerName { get; set; }
    
    /// <inheritdoc />
    public K6ScriptResource(Context context, string containerName, ParameterResource scriptFileName) : base(context.Name)
    {
        ScriptFileParameter = scriptFileName;
        ContainerName = containerName;
    }
    
    public ReferenceExpression ScriptFileReference => ReferenceExpression.Create($"{ScriptFileParameter}");
    
    public ParameterResource ScriptFileParameter { get; set; }
}