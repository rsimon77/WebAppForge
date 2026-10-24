namespace WebAppForge.Core.Tests;

/// <summary>
/// SHA-256 (first 16 hex digits, line endings normalized to LF) of every file the generator wrote for the three sample projects in
/// <see cref="CustomizationSamples"/> before customization existed (appsettings.json is checked separately because it holds a path).
/// The three OrderItemsPages.cs hashes were updated deliberately when decimal inputs gained step="any" (the only change in the output).
/// The three Program.cs hashes were updated deliberately when the default site title changed from the old product name to "Web Application" (the only change in the output).
/// Every hash was updated deliberately when the generated sites moved to Dapper (ADR 001): models lost FromReader, queries go through
/// Dapper, select lists name each column after its property, and Web/Db.cs was added.
/// </summary>
internal static class CustomizationGolden
{
    public const string Hashes = """
        localized/Endpoints/AuditLogEndpoints.cs b89eacb2695d93b4
        localized/Endpoints/CodesEndpoints.cs d3be228f865f83ae
        localized/Endpoints/NotesEndpoints.cs 34eedaa5adb391a2
        localized/Endpoints/OrderItemsEndpoints.cs 0c38d77d5a8e2bc1
        localized/Endpoints/OrderLinesEndpoints.cs d4c8dfc80a951e9d
        localized/Endpoints/UsersEndpoints.cs 4dcca3ed7d874f84
        localized/LocShop.csproj 2dc238918d7c97f0
        localized/Localization/Strings.cs 3df468507f47da20
        localized/Localization/strings.de.json ca3d163bab055381
        localized/Localization/strings.en.json 01e0fcb7a87740e2
        localized/Models/AuditLog.cs a70a54b8072eba00
        localized/Models/Codes.cs 5622dce221ed3c93
        localized/Models/Notes.cs 8c5a4b4ed0c92dff
        localized/Models/OrderItems.cs d73559c58170c305
        localized/Models/OrderLines.cs a54c589426fde6be
        localized/Models/Users.cs e93c067f5c2991e2
        localized/Pages/AuditLogPages.cs 757744acbfc5fee1
        localized/Pages/AuthPages.cs 378f0e84f5102ce8
        localized/Pages/CodesPages.cs 807fe3ec487844ff
        localized/Pages/NotesPages.cs 430c17ea644db3e4
        localized/Pages/OrderItemsPages.cs a6028474a0bcfb34
        localized/Pages/OrderLinesPages.cs c495ab348d33be12
        localized/Pages/UserAccountPages.cs 68b4e82f64cc8414
        localized/Pages/UsersPages.cs 09192a7048838baf
        localized/Program.cs bc5ba39fff17367f
        localized/Security/LoginAttempts.cs 131d5392d619d1a0
        localized/Security/Passwords.cs 41009637792cf9b8
        localized/Security/Permissions.cs 654c0e972b82fbfb
        localized/Web/Db.cs b5489f929792b77d
        localized/Web/Html.cs 5d4916cd46de86d2
        plain/Endpoints/AuditLogEndpoints.cs 215162fd8fe2cdaf
        plain/Endpoints/CodesEndpoints.cs 895428f2300cc5e5
        plain/Endpoints/OrderItemsEndpoints.cs d43b76ae0538af38
        plain/Endpoints/OrderLinesEndpoints.cs 06b8aeef30daf649
        plain/Models/AuditLog.cs 89845767ceeaf724
        plain/Models/Codes.cs 9974b0abffc8903d
        plain/Models/OrderItems.cs 80cb11a29ad65a84
        plain/Models/OrderLines.cs 084d138737735c2a
        plain/Pages/AuditLogPages.cs 8bc5ff70eb8b43a4
        plain/Pages/CodesPages.cs 82b5f5ab2624d494
        plain/Pages/OrderItemsPages.cs 1a12209e5b910db7
        plain/Pages/OrderLinesPages.cs dd95cc9ffc96567e
        plain/Program.cs 9cefde45c31ad182
        plain/ShopDemo.csproj 2dc238918d7c97f0
        plain/Web/Db.cs 7dc6734d435284db
        plain/Web/Html.cs 875867fa661f34dc
        secure/Endpoints/AuditLogEndpoints.cs adddef8d9b078632
        secure/Endpoints/CodesEndpoints.cs 9d33069353064dc0
        secure/Endpoints/NotesEndpoints.cs e6e395da0f33393e
        secure/Endpoints/OrderItemsEndpoints.cs f74020e0f90080bf
        secure/Endpoints/OrderLinesEndpoints.cs ac4158c0c159a9a2
        secure/Endpoints/UsersEndpoints.cs e055f1f431934385
        secure/Models/AuditLog.cs 238e218acddfc2bb
        secure/Models/Codes.cs 2eac009c2c073d6a
        secure/Models/Notes.cs 286c8ea1b814e826
        secure/Models/OrderItems.cs 0d45fedb433cdd6b
        secure/Models/OrderLines.cs d304d0a1e6bca597
        secure/Models/Users.cs 343ec4284f8a8275
        secure/Pages/AuditLogPages.cs e80204e2250d354b
        secure/Pages/AuthPages.cs db6f94a7e8387d0e
        secure/Pages/CodesPages.cs 7a7a1788610c7017
        secure/Pages/NotesPages.cs 9f5968382ee1882e
        secure/Pages/OrderItemsPages.cs a63cf66283e455fc
        secure/Pages/OrderLinesPages.cs e377de5b27481ac7
        secure/Pages/UserAccountPages.cs 350def10e05d13e4
        secure/Pages/UsersPages.cs 91b6b65df79bace0
        secure/Program.cs bc000fd92032e96b
        secure/SecureShop.csproj 2dc238918d7c97f0
        secure/Security/LoginAttempts.cs f0f478953fa66f0c
        secure/Security/Passwords.cs 95c9aeabed064bc8
        secure/Security/Permissions.cs 7a1b549c856db7df
        secure/Web/Db.cs 9780c6287a6ff902
        secure/Web/Html.cs 1f83143f611ad441
        """;
}
