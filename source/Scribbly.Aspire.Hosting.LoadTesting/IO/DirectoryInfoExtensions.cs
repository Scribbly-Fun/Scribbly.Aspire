namespace Scribbly.Aspire;

/// <summary>
/// Extensions methods used to setup and configure the file system for the load testing dashboard and K6
/// </summary>
internal static class DirectoryInfoExtensions
{
    /// <summary>
    /// Initializes the script directory with at least 1 K6 JS script.
    /// </summary>
    /// <param name="directory">The directory where the js files must live.</param>
    /// <returns>A list of all the js scripts inside the directory</returns>
    /// <remarks>
    ///     When this directory is not found - it will be created and the example script will be copied into the location
    ///     When this directory is found but contains no scripts - it will copy the example script.
    /// </remarks>
    internal static IEnumerable<FileInfo> InitializeScriptDirectory(this DirectoryInfo directory)
    {
        if (!directory.Exists)
        {
            Directory.CreateDirectory(directory.FullName);
            CopyExampleFileFromResourceStream(directory);
        }

        var files = directory.GetFiles("*js");

        if (files.Length == 0)
        {
            return [ CopyExampleFileFromResourceStream(directory) ];
        }

        return files;
    }

    /// <summary>
    /// Streams the example test script from the manifest resource stream and creates a file in the directory.
    /// </summary>
    /// <param name="directory">Directory for all th scripts.</param>
    /// <returns>File info for the example-test.js</returns>
    /// <exception cref="FileLoadException">If the file is not in the resource stream.</exception>
    private static FileInfo CopyExampleFileFromResourceStream(DirectoryInfo directory)
    {
        const string fileName = "example-test.js";

        var exampleScript = Path.Combine(directory.FullName, fileName);

        if (File.Exists(exampleScript))
        {
            return new FileInfo(exampleScript);
        }
        
        var assembly = typeof(LoadTesterResource).Assembly;
        using var stream = assembly.GetManifestResourceStream($"{ConfigurationFileManager.Namespace}.{fileName}")
                           ?? throw new FileLoadException(fileName);
        
        using var fileStream = File.Create(exampleScript);
        stream.CopyTo(fileStream);
        
        return new FileInfo(exampleScript);
    }
}