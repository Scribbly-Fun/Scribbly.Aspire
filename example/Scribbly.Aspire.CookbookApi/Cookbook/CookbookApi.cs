using Scribbly.Stencil;

namespace Scribbly.Aspire.Web.Cookbook;

[EndpointGroup("/cookbooks", "Cookbook Data")]
public partial class CookbookApi
{
    private record CookBook(string Title, string Author);
    
    [GetEndpoint("/", "GetCookbooks", "Gets the cookbooks")]
    private static IEnumerable<CookBook> GetCookbooks() =>
        Enumerable.Range(1, 5).Select(index =>
                new CookBook
                (
                    $"Title {index}",
                        $"Author {index}"
                ));
}