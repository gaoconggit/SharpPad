using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace SharpPad.Desktop.Services;

public sealed class InstanceManager
{
    private static readonly Lazy<InstanceManager> _instance = new(() => new InstanceManager());
    public static InstanceManager Instance => _instance.Value;

    private const string ProcessName = "SharpPad.Desktop";
    private const int DefaultPort = 5090;
    
    private InstanceManager() { }

    public (int port, bool isFirstInstance) GetPortForInstance()
    {
        var runningInstances = GetRunningInstances();
        var isFirstInstance = runningInstances <= 1;
        
        if (isFirstInstance)
        {
            // 第一个实例使用默认端口，如果可用的话
            if (IsPortAvailable(DefaultPort))
            {
                return (DefaultPort, true);
            }
        }
        
        // 多实例或默认端口不可用时，使用随机可用端口
        var availablePort = GetAvailablePort();
        return (availablePort, false);
    }

    private int GetRunningInstances()
    {
        try
        {
            var currentProcess = Process.GetCurrentProcess();
            var processes = Process.GetProcessesByName(ProcessName);
            
            // 过滤掉当前进程
            var otherInstances = processes.Where(p => p.Id != currentProcess.Id).ToList();
            
            // 清理已结束的进程对象
            foreach (var process in processes)
            {
                process.Dispose();
            }
            
            // 返回总实例数（包括当前实例）
            return otherInstances.Count + 1;
        }
        catch (Exception)
        {
            // 如果获取进程信息失败，默认返回1（当前实例）
            return 1;
        }
    }

    private static int GetAvailablePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private static bool IsPortAvailable(int port)
    {
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch
        {
            return false;
        }
    }
    
    public List<int> GetUsedPortsByOtherInstances()
    {
        var usedPorts = new List<int>();
        try
        {
            var tcpConnInfoArray = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
            usedPorts.AddRange(tcpConnInfoArray.Select(endpoint => endpoint.Port));
        }
        catch (Exception)
        {
            // 忽略获取端口信息失败的情况
        }
        
        return usedPorts;
    }
}