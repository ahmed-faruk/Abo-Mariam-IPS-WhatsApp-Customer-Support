namespace WhatsAppMonitorAssistant.Unit.Tests;

/// <summary>
/// Guards the repository skeleton described in docs/TECHNICAL.md section 3.
/// The test asserts structure only; it is replaced by behavioural unit tests as the
/// feature tickets land.
/// </summary>
public sealed class SolutionSkeletonTests
{
    private const string SolutionFileName = "WhatsAppMonitorAssistant.slnx";

    public static TheoryData<string> DocumentedProjects =>
    [
        "src/Host.Web/Host.Web.csproj",
        "src/BuildingBlocks/Application/BuildingBlocks.Application.csproj",
        "src/BuildingBlocks/Domain/BuildingBlocks.Domain.csproj",
        "src/BuildingBlocks/Messaging/BuildingBlocks.Messaging.csproj",
        "src/Modules/Catalog/Catalog.csproj",
        "src/Modules/Conversations/Conversations.csproj",
        "src/Modules/Identity/Identity.csproj",
        "src/Modules/Intelligence/Intelligence.csproj",
        "src/Modules/Messaging/Messaging.csproj",
        "src/Modules/Storefront/Storefront.csproj",
        "tests/Architecture.Tests/Architecture.Tests.csproj",
        "tests/Contract.Tests/Contract.Tests.csproj",
        "tests/E2E.Tests/E2E.Tests.csproj",
        "tests/Integration.Tests/Integration.Tests.csproj",
        "tests/Performance.Tests/Performance.Tests.csproj",
        "tests/Unit.Tests/Unit.Tests.csproj",
    ];

    public static TheoryData<string> DocumentedModuleLayers =>
    [
        "src/Modules/Catalog/Domain",
        "src/Modules/Catalog/Features",
        "src/Modules/Catalog/Contracts",
        "src/Modules/Catalog/Infrastructure",
        "src/Modules/Conversations/Domain",
        "src/Modules/Conversations/Features",
        "src/Modules/Conversations/Contracts",
        "src/Modules/Conversations/Infrastructure",
        "src/Modules/Identity/Domain",
        "src/Modules/Identity/Features",
        "src/Modules/Identity/Contracts",
        "src/Modules/Identity/Infrastructure",
        "src/Modules/Intelligence/Domain",
        "src/Modules/Intelligence/Features",
        "src/Modules/Intelligence/Contracts",
        "src/Modules/Intelligence/Infrastructure",
        "src/Modules/Messaging/Domain",
        "src/Modules/Messaging/Features",
        "src/Modules/Messaging/Contracts",
        "src/Modules/Messaging/Infrastructure",
        "src/Modules/Storefront/Domain",
        "src/Modules/Storefront/Features",
        "src/Modules/Storefront/Contracts",
        "src/Modules/Storefront/Infrastructure",
    ];

    [Fact]
    public void Repository_root_contains_the_solution_file()
    {
        Assert.True(File.Exists(SolutionPath), $"{SolutionFileName} was not found at {RepositoryRoot}.");
    }

    [Theory]
    [MemberData(nameof(DocumentedProjects))]
    public void Documented_project_exists_and_is_listed_in_the_solution(string relativeProjectPath)
    {
        var projectPath = Path.Combine(RepositoryRoot, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(File.Exists(projectPath), $"{relativeProjectPath} is missing.");
        Assert.Contains($"Path=\"{relativeProjectPath}\"", SolutionContents, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(DocumentedModuleLayers))]
    public void Module_exposes_the_documented_layer_folder(string relativeFolderPath)
    {
        var folderPath = Path.Combine(RepositoryRoot, relativeFolderPath.Replace('/', Path.DirectorySeparatorChar));

        Assert.True(Directory.Exists(folderPath), $"{relativeFolderPath} is missing.");
    }

    private static string RepositoryRoot { get; } = FindRepositoryRoot();

    private static string SolutionPath => Path.Combine(RepositoryRoot, SolutionFileName);

    private static string SolutionContents => File.ReadAllText(SolutionPath);

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"{SolutionFileName} was not found above {AppContext.BaseDirectory}.");
    }
}
