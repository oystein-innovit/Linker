#load build/paths.cake
#load build/version.cake
#load build/package.cake
#load build/urls.cake

#addin "nuget:?package=Cake.Npm&version=0.17.0"
#addin "nuget:?package=Cake.Curl&version=4.1.0"

#tool nuget:?package=GitVersion.CommandLine&version=4.0.0-beta0012
#tool nuget:?package=OctopusTools&version=6.7.0

var target = Argument("Target", "Build");

Setup<PackageMetadata>(context =>
{
    return new PackageMetadata(
        outputDirectory: Argument("packageOutputDirectory", "packages"),
        name: "Linker-5"
    );
});

Task("Compile")
    .Does(() =>
    {
        Information("..... Compiling " + Paths.SolutionFile.FullPath);
        DotNetCoreBuild(Paths.SolutionFile.FullPath);
    })
    .OnError(exception =>
    {
        Information("Det trynte i compile");
    });

Task("Build-Frontend")
    .Does(() =>
{
    NpmInstall(settings => settings.FromPath(Paths.FrontendDirectory.FullPath));
    NpmRunScript("build", settings => settings.FromPath(Paths.FrontendDirectory.FullPath));
});

Task("Test")
    .IsDependentOn("Compile")
    .Does(() =>
{
    DotNetCoreTest(
        Paths.TestProjectFile.FullPath,
        new DotNetCoreTestSettings
        {
            Logger = "trx", // VS Test result format
            ResultsDirectory = Paths.TestResultDirectory
        }
    );
});

Task("Package-zip")
    .IsDependentOn("Test")
    .IsDependentOn("Build-Frontend")
    .IsDependentOn("Version")
    .Does<PackageMetadata>(package =>
{
    CleanDirectory(package.OutputDirectory);
    package.Extension = "zip";
    DotNetCorePublish(
        Paths.WebProjectFile.GetDirectory().FullPath,
        new DotNetCorePublishSettings 
        {
            OutputDirectory = Paths.PublishDirectory,
            NoBuild = true,
            NoRestore = true,
            MSBuildSettings = new DotNetCoreMSBuildSettings {
                NoLogo = true
            }
        }
    );

    Zip(Paths.PublishDirectory, package.FullPath);
});

Task("Package-Octopus")
    .IsDependentOn("Test")
    .IsDependentOn("Build-Frontend")
    .IsDependentOn("Version")
    .Does<PackageMetadata>(package =>
{
    CleanDirectory(package.OutputDirectory);
    package.Extension = "nupkg";
    DotNetCorePublish(
        Paths.WebProjectFile.GetDirectory().FullPath,
        new DotNetCorePublishSettings 
        {
            OutputDirectory = Paths.PublishDirectory,
            NoBuild = true,
            NoRestore = true,
            MSBuildSettings = new DotNetCoreMSBuildSettings {
                NoLogo = true
            }
        }
    );

    OctoPack(
        package.Name,
        new OctopusPackSettings {
            Format = OctopusPackFormat.NuPkg,
            Version = package.Version,
            BasePath = Paths.PublishDirectory,
            OutFolder = package.OutputDirectory
        }
    );
});

Task("Deploy-Kudu")
    .IsDependentOn("Package-Zip")
    .Does<PackageMetadata>(package =>
{
    CurlUploadFile(
        package.FullPath,
        Urls.KuduDeployUrl,
        new CurlSettings
        {
            Username = EnvironmentVariable("DeploymentUser"),
            Password = EnvironmentVariable("DeploymentPassword"),
            RequestCommand = "POST",
            ArgumentCustomization = args => args.Append("--fail")
        }
    );
});

Task("Deploy-Octopus")
    .IsDependentOn("Package-Octopus")
    .Does<PackageMetadata>( package =>
{
    OctoPush(
        Urls.OctopusDeployUrl.AbsoluteUri,
        EnvironmentVariable("OctopusApiKey"),
        package.FullPath,
        new OctopusPushSettings {
            EnableServiceMessages = true
        }
    );

    OctoCreateRelease(
        "Linker-5",
        new CreateReleaseSettings
        {
            Server = Urls.OctopusDeployUrl.AbsoluteUri,
            ApiKey = EnvironmentVariable("OctopusApiKey"),
            ReleaseNumber = package.Version,
            DefaultPackageVersion = package.Version,
            DeployTo = "Test",
            IgnoreExisting = true,
            DeploymentProgress = true,
            WaitForDeployment = true
        }
    );
});

Task("Version")
    .Does<PackageMetadata>(package =>
{
    package.Version  = ReadVersionFromProjectFile(Context);
    if (package.Version == null) {
        Information($"Version info missing in project file");
        package.Version = GitVersion().FullSemVer;
    }
    Information($"Version {package.Version}");
});

Task("Set-Build-Number")
    .WithCriteria(() => BuildSystem.IsRunningOnTeamCity)
    .Does<PackageMetadata>( package =>
{
    var buildNumber = TeamCity.Environment.Build.Number;
    TeamCity.SetBuildNumber($"{package.Version}+{buildNumber}");
});

Task("Publish-Build-Artifact")
    .WithCriteria(BuildSystem.IsRunningOnTeamCity)
    .IsDependentOn("Package-zip")
    .Does<PackageMetadata>( package =>
{
    TeamCity.PublishArtifacts(package.FullPath);

    foreach( var p in GetFiles(package.OutputDirectory + $"/*.{package.Extension}")) {
        TeamCity.PublishArtifacts(p.FullPath);
    }
});

Task("Publish-Test-Results")
    .WithCriteria(BuildSystem.IsRunningOnTeamCity)
    .IsDependentOn("Test")
    .Does(() =>
{
    /*TFBuild.Commands.PublishTestResults(
        new TFBuildPublishTestResultsData
        {
            TestRunner = TFTestRunnerType.VSTest,
            TestResultsFiles = GetFiles(Paths.TestResultDirectory + "/*.trx").ToList()
        }
    );*/
    foreach(var testresult in GetFiles(Paths.TestResultDirectory + "/*.trx"))
    {
        TeamCity.ImportData("vstest", testresult);
    }
    
});

Task("Build-CI")
    .IsDependentOn("Compile")
    .IsDependentOn("Test")
    .IsDependentOn("Build-Frontend")
    .IsDependentOn("Version")
    .IsDependentOn("Package-Zip")
    .IsDependentOn("Set-Build-Number")
    .IsDependentOn("Publish-Build-Artifact")
    .IsDependentOn("Publish-Test-Results");

RunTarget(target);