using FluentAssertions;
using Sharpliner.AzureDevOps;
using Xunit;

namespace Sharpliner.Tests;

public class ReadmeGeneratorTests
{

    class PullRequestPipeline : SingleStagePipelineDefinition
    {
        // Say where to publish the YAML to
        public override string TargetFile => "eng/pr.yml";
        public override TargetPathType TargetPathType => TargetPathType.RelativeToGitRoot;

        public override SingleStagePipeline Pipeline => new()
        {
            Pr = new PrTrigger("main"),

            Variables =
            [
                // YAML ${{ if }} conditions are available with handy macros that expand into the
                // expressions such as comparing branch names. We also have "else"
                If.IsBranch("net-6.0")
                    .Variable("DotnetVersion", "6.0.100")
                    .Group("net6-keyvault")
                .Else
                    .Variable("DotnetVersion", "5.0.202"),
            ],

            Jobs =
            [
                new Job("Build")
            {
                Pool = new HostedPool("Azure Pipelines", "windows-latest"),
                Steps =
                [
                    // Many tasks have helper methods for shorter notation
                    DotNet.Install.Sdk(variables["DotnetVersion"]),

                    // You can also specify any pipeline task in full too
                    Task("DotNetCoreCLI@2", "Build and test") with
                    {
                        Inputs = new()
                        {
                            { "command", "test" },
                            { "projects", "src/MyProject.sln" },
                        }
                    },

                    // Frequently used ${{ if }} statements have readable macros
                    If.IsPullRequest
                        // You can load script contents from a .ps1 file and inline them into YAML
                        // This way you can write scripts with syntax highlighting separately
                        .Step(Powershell.FromResourceFile("New-Report.ps1", "Create build report")),
                ]
            }
            ],
        };
    }

    [Fact]
    public void Test_PullRequestPipeline()
    {
        var yaml = new PullRequestPipeline().Serialize();
        yaml.Trim().Should().Be("""
            pr:
              branches:
                include:
                - main

            variables:
            - ${{ if eq(variables['Build.SourceBranch'], 'refs/heads/net-6.0') }}:
              - name: DotnetVersion
                value: 6.0.100

              - group: net6-keyvault

            - ${{ else }}:
              - name: DotnetVersion
                value: 5.0.202

            jobs:
            - job: Build
              pool:
                name: Azure Pipelines
                vmImage: windows-latest
              steps:
              - task: UseDotNet@2
                inputs:
                  packageType: sdk
                  version: $(DotnetVersion)

              - task: DotNetCoreCLI@2
                displayName: Build and test
                inputs:
                  command: test
                  projects: src/MyProject.sln

              - ${{ if eq(variables['Build.Reason'], 'PullRequest') }}:
                - powershell: |+
                    Write-Host 'Creating build report'
                  displayName: Create build report
            """);
    }
}
