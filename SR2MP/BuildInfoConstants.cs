#pragma warning disable RCS1110 // Declare type inside namespace
// ReSharper disable once CheckNamespace

internal static class BuildInfo
{
    internal const string ID = "de.pyeight.ranchingtogether";
    internal const string Name = "Ranching Together";
    internal const string Description = "Adds Multiplayer to Slime Rancher 2";
    internal const string Author = "Shark (py8)";
    internal static readonly string[] CoAuthors = null;
    internal static readonly string[] Contributors = new[] { "AlchlcSystm, PinkTarr", "Ryuiku (Patched fork)" };
    // MelonVersion is shown by ML on startup
    // Version is shown by Starlight
    // Version automatically gets a -dev at the end if SR2MP is compiled by GitHub Action
    // MelonVersion must stay numeric (it is also the assembly version), so the fork tag lives in Version.
    internal const string MelonVersion = "0.3.9";
    internal const string Version = "0.3.9-Patched.2"; // Auto-Dev_Do_not_remove
    internal const string Discord = "https://discord.com/invite/a7wfBw5feU";
    // This build is the "Patched" fork of Ranching Together. Original mod: https://github.com/pyeight/SlimeRancher2Multiplayer
    internal const string UpstreamSourceCode = "https://github.com/pyeight/SlimeRancher2Multiplayer";
    internal const string SourceCode = "https://github.com/Ryuiku/SlimeRancher2Multiplayer";
    internal const string Releases = SourceCode + "/releases/latest";
    internal const string Nexus = "https://www.nexusmods.com/slimerancher2/mods/118";
    internal const bool UsePrism = false;
    internal const string MinimumStarlightVersion = Starlight.BuildInfo.CodeVersion; // e.g "3.6.3", the min required SR2 version. No beta or alpha versions
    internal const string MinimumGameVersion = "1.3.0"; // e.g 1.1.0 or something similar (optional)
    internal const string ExactGameVersion = "1.3.0"; // e.g 1.1.0 or something similar (optional)
}