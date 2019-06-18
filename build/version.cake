//#addin nuget:?package=Cake.Git&version=0.19.0

#load paths.cake

using System.Text.RegularExpressions;

public static string ReadVersionFromProjectFile(ICakeContext context)
{
    var versionNode = "/Project/PropertyGroup/Version/text()";
    var version = context.XmlPeek(Paths.WebProjectFile, versionNode, new XmlPeekSettings{SuppressWarning = true});
    return version;
}

/*public static bool LatestCommitHasVersionTag(this ICakeContext context)
{
    var latestTag = context.GitDescribe(Paths.Directory);
    var isVersionTag = Regex.IsMatch(latestTag, @"v[0-9]*");
    var noCommitsAfterLatestTag = !Regex.IsMatch(latestTag, @"\-[0-9]+\-");

    return isVersionTag && noCommitsAfterLatestTag;
}*/
