# 用户输入测试

这个示例演示了如何在 SharpPad 中使用 Console.ReadLine() 接收用户输入。

```csharp
using System;

class Program
{
    static void Main()
    {
        Console.WriteLine("=== 用户输入测试程序 ===");
        Console.WriteLine();
        
        // 测试1：基本输入
        Console.Write("请输入您的名字: ");
        string name = Console.ReadLine();
        Console.WriteLine($"您好, {name}!");
        Console.WriteLine();
        
        // 测试2：数字输入
        Console.Write("请输入您的年龄: ");
        string ageInput = Console.ReadLine();
        if (int.TryParse(ageInput, out int age))
        {
            Console.WriteLine($"您 {age} 岁了。");
            if (age >= 18)
            {
                Console.WriteLine("您已经成年了！");
            }
            else
            {
                Console.WriteLine($"再过 {18 - age} 年您就成年了。");
            }
        }
        else
        {
            Console.WriteLine("输入的年龄格式不正确。");
        }
        Console.WriteLine();
        
        // 测试3：选择输入
        Console.WriteLine("请选择您喜欢的编程语言:");
        Console.WriteLine("1. C#");
        Console.WriteLine("2. Python");
        Console.WriteLine("3. JavaScript");
        Console.WriteLine("4. Java");
        Console.Write("请输入选项 (1-4): ");
        string choice = Console.ReadLine();
        
        string language = choice switch
        {
            "1" => "C#",
            "2" => "Python",
            "3" => "JavaScript",
            "4" => "Java",
            _ => "未知语言"
        };
        
        Console.WriteLine($"您选择了: {language}");
        Console.WriteLine();
        
        Console.WriteLine("测试完成！");
    }
}
```

## 使用说明

1. 点击"运行"按钮执行代码
2. 当看到输入提示时，在出现的输入框中输入内容
3. 点击"发送"按钮或按回车键提交输入
4. 程序会继续执行并显示结果

## 注意事项

- 确保在看到输入提示后再输入内容
- 每次只能输入一行文本
- 输入框会在您输入后自动消失
