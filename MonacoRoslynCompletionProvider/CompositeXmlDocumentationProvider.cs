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
    /// 组合多个 XML 文档提供者，用于从多个 XML 文件中查找文档
    /// </summary>
    public class CompositeXmlDocumentationProvider : XmlDocumentationProvider
    {
        private readonly List<XmlDocumentationProvider> _providers;
        private readonly MethodInfo _getDocumentationMethod;

        public CompositeXmlDocumentationProvider(List<string> xmlFilePaths)
        {
            _providers = xmlFilePaths
                .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
                .Select(path => XmlDocumentationProvider.CreateFromFile(path))
                .ToList();
            
            // 使用反射获取受保护的方法
            _getDocumentationMethod = typeof(XmlDocumentationProvider)
                .GetMethod("GetDocumentationForSymbol", BindingFlags.NonPublic | BindingFlags.Instance);
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

        public override bool Equals(object obj)
        {
            return obj is CompositeXmlDocumentationProvider other && 
                   _providers.Count == other._providers.Count;
        }

        public override int GetHashCode()
        {
            return _providers.Count.GetHashCode();
        }
    }
}