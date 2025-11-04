using Aspire.Hosting.ApplicationModel;
using Scribbly.Aspire.K6;

namespace Scribbly.Aspire;

public class LoadTesterResource : Resource
{
    internal string Directory { get; }
    internal K6ServerResource? Server { get; private set; }
    
    /// <inheritdoc />
    internal LoadTesterResource(string name, string directory) : base(name)
    {
        Directory = directory;
    }

    internal void AddK6Server(K6ServerResource server)
    {
        Server = server;
    }
}