using System;

namespace MultiFileExample
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("Multi-File Compilation Example");
            Console.WriteLine("==============================");

            // 使用 Calculator 类
            var calculator = new Calculator();
            int result = calculator.Add(10, 20);
            Console.WriteLine($"10 + 20 = {result}");

            result = calculator.Multiply(5, 6);
            Console.WriteLine($"5 × 6 = {result}");

            // 使用 Person 类
            var person = new Person("张三", 25);
            person.Introduce();

            // 使用 StringHelper 静态类
            string text = "Hello World";
            string reversed = StringHelper.Reverse(text);
            Console.WriteLine($"'{text}' reversed is '{reversed}'");

            Console.WriteLine("\n多文件编译成功！");
        }
    }
}