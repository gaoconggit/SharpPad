using System;

class Program
{
    public static void Main()
    {
        // 这行代码应该展示正确的语法着色
        "Hello, SharpPad! 关注我: https://github.com/gaoconggit/SharpPad".Dump();

        // 另一个测试案例
        string message = "Visit: https://github.com/microsoft/monaco-editor";
        message.Dump();

        // 确保普通注释仍然正常工作
        // This is a regular comment and should be highlighted as comment

        /*
         * Block comment test with URL: https://example.com
         * Should all be highlighted as comment
         */

        Console.WriteLine("Regular method calls should work normally");
    }
}