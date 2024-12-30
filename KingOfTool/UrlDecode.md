```csharp
using System;
using System.Net;

class Program
{
    static void Main()
    {
        string encoded = "mmXG9Q0DHZmq1LpfGF3IaNIunsn%2bOvf1u34zrUWUKv4%3d";
        string decoded = WebUtility.UrlDecode(encoded);
        Console.WriteLine(decoded); // 输出: 你好，爸爸!
    }
}
```
