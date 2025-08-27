```csharp
using System;
using System.Threading.Tasks;
using System.Diagnostics;

class Program
{
    public static void Main(string[] args)
    {
        //启动调试器并打上断点
        Debugger.Launch();
        Debugger.Break();
        if (!Debugger.IsAttached)
        {
            Console.WriteLine("因为没有调试器附加，程序将退出");
            Environment.Exit(0);
        }

        //你的代码
        int a = 1 + 1;
        Console.WriteLine(a);
    }
}
```
