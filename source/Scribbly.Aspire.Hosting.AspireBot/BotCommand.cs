namespace Scribbly.Aspire;

internal record BotCommand(
    string Path,
    BotCommand.RequestMethod Method = BotCommand.RequestMethod.Get,
    TimeSpan? Interval = null,
    Func<HttpContent>? ContentRequestDelegate = null,
    Func<HttpResponseMessage, ValueTask<object?>>? ResponseHandler = null)
{
    internal enum RequestMethod
    {
        Get,
        Post,
        Put,
        Delete
    }

    public HttpMethod HttpMethod => Method switch
    {
        RequestMethod.Get => HttpMethod.Get,
        RequestMethod.Post => HttpMethod.Post,
        RequestMethod.Put => HttpMethod.Put,
        RequestMethod.Delete => HttpMethod.Delete,
        _ => throw new ArgumentOutOfRangeException()
    };
    
    public bool IsPollingCommand => Interval != null;
};