namespace WebAppForge.Core.Tests;

/// <summary>
/// SHA-256 (first 16 hex digits, line endings normalized to LF) of every file the generator wrote for the three sample projects in
/// <see cref="CustomizationSamples"/> before customization existed (appsettings.json is checked separately because it holds a path).
/// The three OrderItemsPages.cs hashes were updated deliberately when decimal inputs gained step="any" (the only change in the output).
/// The three Program.cs hashes were updated deliberately when the default site title changed from the old product name to "Web Application" (the only change in the output).
/// </summary>
internal static class CustomizationGolden
{
    public const string Hashes = """
        localized/Endpoints/AuditLogEndpoints.cs 8e9ae5f0bff5474c
        localized/Endpoints/CodesEndpoints.cs 1d6944d439730410
        localized/Endpoints/NotesEndpoints.cs 8297cdb63c421dd1
        localized/Endpoints/OrderItemsEndpoints.cs 88111b813cf3299d
        localized/Endpoints/OrderLinesEndpoints.cs 82ba7437b60ac20f
        localized/Endpoints/UsersEndpoints.cs eb972613aef33620
        localized/LocShop.csproj 38805c8bae2c2049
        localized/Localization/Strings.cs 3df468507f47da20
        localized/Localization/strings.de.json ca3d163bab055381
        localized/Localization/strings.en.json 01e0fcb7a87740e2
        localized/Models/AuditLog.cs b352249bb1ebd7b1
        localized/Models/Codes.cs a722b91d30d30975
        localized/Models/Notes.cs 8fac9c065ed385a4
        localized/Models/OrderItems.cs 01879f2a16a71dc8
        localized/Models/OrderLines.cs 039893d6f5593ebd
        localized/Models/Users.cs 2ab6b365f7796211
        localized/Pages/AuditLogPages.cs cf1e2945f3d43c8a
        localized/Pages/AuthPages.cs 57b172548472a516
        localized/Pages/CodesPages.cs d4d322f0874ba1e9
        localized/Pages/NotesPages.cs 725ca1bb90298640
        localized/Pages/OrderItemsPages.cs 6c4dff75ef5c12aa
        localized/Pages/OrderLinesPages.cs 244c9e0c0520caf1
        localized/Pages/UserAccountPages.cs 4fbc6298fa4c1417
        localized/Pages/UsersPages.cs 09c246c5dfe17f53
        localized/Program.cs 84a2a866fb82c717
        localized/Security/LoginAttempts.cs d2f8ce380c55b236
        localized/Security/Passwords.cs 41009637792cf9b8
        localized/Security/Permissions.cs 654c0e972b82fbfb
        localized/Web/Html.cs 5d4916cd46de86d2
        plain/Endpoints/AuditLogEndpoints.cs 47efdeb502a4abca
        plain/Endpoints/CodesEndpoints.cs 8e0da726d813a3dc
        plain/Endpoints/OrderItemsEndpoints.cs 9c487eced9a5062f
        plain/Endpoints/OrderLinesEndpoints.cs c5118f7caa62ce0a
        plain/Models/AuditLog.cs 56b2f71769b43169
        plain/Models/Codes.cs 9bc04d67823fd217
        plain/Models/OrderItems.cs 3b3e15d6c7998634
        plain/Models/OrderLines.cs f19764644900da57
        plain/Pages/AuditLogPages.cs 754e776c3dabfc36
        plain/Pages/CodesPages.cs 9546479720e204b3
        plain/Pages/OrderItemsPages.cs da065ae8e01cdb9b
        plain/Pages/OrderLinesPages.cs 451da66d7f7d19e7
        plain/Program.cs c6567929304dfbf9
        plain/ShopDemo.csproj 38805c8bae2c2049
        plain/Web/Html.cs 875867fa661f34dc
        secure/Endpoints/AuditLogEndpoints.cs 6e1b6101f022beca
        secure/Endpoints/CodesEndpoints.cs e30c83a14f1cc63c
        secure/Endpoints/NotesEndpoints.cs 9612ed6d35fd6b25
        secure/Endpoints/OrderItemsEndpoints.cs ee01903ec8a92011
        secure/Endpoints/OrderLinesEndpoints.cs 28f9b92eeb79d65d
        secure/Endpoints/UsersEndpoints.cs c799ba03a8db9b89
        secure/Models/AuditLog.cs d25b81823bb848e6
        secure/Models/Codes.cs fc4b73e0eac00492
        secure/Models/Notes.cs 770030cca085e64d
        secure/Models/OrderItems.cs ca51b072a1c95b3b
        secure/Models/OrderLines.cs 17b82584fc3c1ea9
        secure/Models/Users.cs 58b9a3163ab65503
        secure/Pages/AuditLogPages.cs 615aeef89c0069b6
        secure/Pages/AuthPages.cs c249d112bbab3072
        secure/Pages/CodesPages.cs 77cfd1ce351b2fea
        secure/Pages/NotesPages.cs d343243f7bf43133
        secure/Pages/OrderItemsPages.cs 7924934429fa9ead
        secure/Pages/OrderLinesPages.cs 6cdc03c83a136e81
        secure/Pages/UserAccountPages.cs 44bdf9b8595e6ec5
        secure/Pages/UsersPages.cs cbe4a125453d0b91
        secure/Program.cs 2e1c0fb537bb234c
        secure/SecureShop.csproj 38805c8bae2c2049
        secure/Security/LoginAttempts.cs 940efc711a8b20bc
        secure/Security/Passwords.cs 95c9aeabed064bc8
        secure/Security/Permissions.cs 7a1b549c856db7df
        secure/Web/Html.cs 1f83143f611ad441
        """;
}
