```csharp
using System;
using System.Text.RegularExpressions;

class Program
{
    static void Main()
    {
        //在vscode软件上搜索扩展,点击扩展,右键复制
        string input = 
        """
        Name: C# Dev Kit
        Id: ms-dotnettools.csdevkit
        Description: Official C# extension from Microsoft
        Version: 1.17.27
        Publisher: Microsoft
        VS Marketplace Link: https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit
        """;

        // 提取id和版本
        string idPattern = @"Id: (.+)";
        string versionPattern = @"Version: (.+)";

        var idMatch = Regex.Match(input, idPattern);
        var versionMatch = Regex.Match(input, versionPattern);

        if (idMatch.Success && versionMatch.Success)
        {
            string id = idMatch.Groups[1].Value;
            string version = versionMatch.Groups[1].Value;

            string publisher = id.Split(".")[0];
            string extensionName = id.Split(".")[1];

            var template = $"https://marketplace.visualstudio.com/_apis/public/gallery/publishers/{publisher}/vsextensions/{extensionName}/{version}/vspackage?targetPlatform=win32-x64";
            //The currently available platforms are:
            // win32-x64, win32-arm64, linux-x64, linux-arm64, linux-armhf, alpine-x64, alpine-arm64, darwin-x64, darwin-arm64 and web
            Console.WriteLine(template);
        }
    }
}
```
