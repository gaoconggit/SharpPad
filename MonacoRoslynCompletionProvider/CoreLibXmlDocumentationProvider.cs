using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

namespace MonacoRoslynCompletionProvider
{
    /// <summary>
    /// 专门为 System.Private.CoreLib 提供的全量文档提供者
    /// 加载所有可用的 .NET 参考程序集 XML 文档，确保最大程度的文档覆盖
    /// </summary>
    public class CoreLibXmlDocumentationProvider : XmlDocumentationProvider
    {
        private static readonly Lazy<CoreLibXmlDocumentationProvider> _instance = 
            new Lazy<CoreLibXmlDocumentationProvider>(() => new CoreLibXmlDocumentationProvider());
        
        public static CoreLibXmlDocumentationProvider Instance => _instance.Value;
        
        private readonly List<XmlDocumentationProvider> _providers;
        private readonly MethodInfo _getDocumentationMethod;

        private CoreLibXmlDocumentationProvider()
        {
            _providers = LoadAllAvailableXmlDocuments();
            
            // 使用反射获取受保护的方法
            _getDocumentationMethod = typeof(XmlDocumentationProvider)
                .GetMethod("GetDocumentationForSymbol", BindingFlags.NonPublic | BindingFlags.Instance);
        }

        private List<XmlDocumentationProvider> LoadAllAvailableXmlDocuments()
        {
            var providers = new List<XmlDocumentationProvider>();
            
            try
            {
                // 检测当前 .NET 版本
                var dotnetVersion = Environment.Version;
                var majorVersion = dotnetVersion.Major;
                
                // 构建参考程序集路径
                var dotnetPacksPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), 
                    "dotnet", "packs", "Microsoft.NETCore.App.Ref");
                
                if (!Directory.Exists(dotnetPacksPath))
                    return providers;
                
                // 查找对应版本的参考程序集
                var versions = Directory.GetDirectories(dotnetPacksPath)
                    .Select(d => Path.GetFileName(d))
                    .Where(v => v.StartsWith($"{majorVersion}."))
                    .OrderByDescending(v => v)
                    .ToList();
                
                if (versions.Count == 0)
                    return providers;
                
                var refPath = Path.Combine(dotnetPacksPath, versions[0], "ref", $"net{majorVersion}.0");
                
                if (!Directory.Exists(refPath))
                    return providers;
                
                // 加载所有 XML 文档文件
                var xmlFiles = Directory.GetFiles(refPath, "*.xml", SearchOption.TopDirectoryOnly);
                
                foreach (var xmlFile in xmlFiles)
                {
                    try
                    {
                        var provider = XmlDocumentationProvider.CreateFromFile(xmlFile);
                        providers.Add(provider);
                    }
                    catch
                    {
                        // 忽略加载失败的文档文件
                    }
                }
            }
            catch
            {
                // 如果加载过程中出现任何错误，返回空列表
            }
            
            return providers;
        }

        protected override string GetDocumentationForSymbol(string documentationMemberID, CultureInfo preferredCulture, CancellationToken cancellationToken = default)
        {
            // 尝试从每个提供者中查找文档
            foreach (var provider in _providers)
            {
                try
                {
                    var documentation = (string)_getDocumentationMethod?.Invoke(provider, new object[] { documentationMemberID, preferredCulture, cancellationToken });
                    if (!string.IsNullOrEmpty(documentation))
                    {
                        return documentation;
                    }
                }
                catch
                {
                    // 继续尝试下一个提供者
                }
            }
            
            return string.Empty;
        }

        protected override Stream GetSourceStream(CancellationToken cancellationToken)
        {
            // 对于组合提供者，返回空流
            return Stream.Null;
        }

        public int LoadedDocumentCount => _providers.Count;
        
        public override bool Equals(object obj)
        {
            return obj is CoreLibXmlDocumentationProvider;
        }

        public override int GetHashCode()
        {
            return typeof(CoreLibXmlDocumentationProvider).GetHashCode();
        }
    }
}