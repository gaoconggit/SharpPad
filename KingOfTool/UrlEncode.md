```csharp
using System;
using System.Net;

class Program
{
    static void Main()
    {
        string original = "你好，爸爸!";
        string encoded = WebUtility.UrlEncode(original);
        Console.WriteLine(encoded); // 输出: %E4%BD%A0%E5%A5%BD%EF%BC%8C%E7%88%B8%E7%88%B8%21
    }
}
```
