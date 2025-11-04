# Scribbly.Aspire
Useful dotnet Aspire extensions and Resources.

# Extensions

The Scribbly.Aspire repo has several packages with the intention to be used as helpful extensions for existing Aspire resources. 
this may include everything from styling extensions, custom dialogs, and shared resouces. 

# Resources

The Scribbly.Aspire repo has several packages intended to be used as Aspire resources. 
All resource libraries are prefixed with the Scribbly.Aspire.Hosting name. 

## Load Tester

The Scribbly.Aspire.Hosting.LoadTest resource can be used to run K6 load tests and output the data to a realtime grafana dashboard. 
This resource can be configured at runtime, used to generate load test scripts from C# and discovery scripts from a provided directory. 

![Scribbly.Aspire.Hosting.LoadTest](./source/Scribbly.Aspire.Hosting.LoadTesting/README.md)

# Aspire Links

[Fowler Aspire Gist](https://gist.github.com/davidfowl/b408af870d4b5b54a28bf18bffa127e1)

[Example Resource](https://github.com/dotnet/aspire-samples/tree/main/samples/CustomResources)