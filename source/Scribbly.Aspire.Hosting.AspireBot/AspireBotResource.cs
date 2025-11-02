using Aspire.Hosting.ApplicationModel;

namespace Scribbly.Aspire;

public class AspireBotResource(string name) : Resource(name), 
    IResourceWithEnvironment, 
    IResourceWithParameters,
    IResourceWithArgs, 
    IResourceWithWaitSupport
{
    internal Dictionary<EndpointReference, List<BotCommand>> EndpointCommands = new();

    internal IEnumerable<UrlSnapshot> Urls => EndpointCommands
        .Select(cmd => 
            new UrlSnapshot(
                cmd.Key.Resource.Name, 
                cmd.Value.First().Path,
                false))
        .ToList();
    
    private volatile bool _isRunning;

    public bool IsRunning
    {
        get => _isRunning;
        internal set => _isRunning = value;
    }

    internal void AddCommand(EndpointReference endpoint, BotCommand command)
    {
        if (EndpointCommands.TryGetValue(endpoint, out var commands))
        {
            commands.Add(command);
            return;
        }

        EndpointCommands.Add(endpoint, [command]);
    }

    /// <inheritdoc />
    public IDictionary<string, object?> Parameters => EndpointCommands.ToDictionary(
        k => k.Key.Resource.Name, 
        v => v.Value.FirstOrDefault() as object);
}