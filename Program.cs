using SharpPad.Services;

Console.WriteLine("=== SharpPad 多实例管理器测试 ===");
Console.WriteLine();

// 测试获取端口分配
var (port, isFirstInstance) = InstanceManager.Instance.GetPortForInstance();

Console.WriteLine($"分配的端口: {port}");
Console.WriteLine($"是否为第一个实例: {(isFirstInstance ? "是" : "否")}");
Console.WriteLine();

// 显示当前使用的端口
var usedPorts = InstanceManager.Instance.GetUsedPortsByOtherInstances();
Console.WriteLine($"当前系统使用的端口数量: {usedPorts.Count}");

if (usedPorts.Count > 0)
{
    Console.WriteLine("部分使用中的端口:");
    var relevantPorts = usedPorts.Where(p => p >= 5000 && p <= 6000).Take(10);
    foreach (var usedPort in relevantPorts)
    {
        Console.WriteLine($"  - {usedPort}");
    }
}

Console.WriteLine();
Console.WriteLine("模拟启动服务器...");
Console.WriteLine($"模拟在端口 {port} 启动 SharpPad 服务器");
Console.WriteLine($"访问 URL: http://localhost:{port}");
Console.WriteLine($"实例类型: {(isFirstInstance ? "主实例" : "副实例")}");

Console.WriteLine();
Console.WriteLine("测试完成！");