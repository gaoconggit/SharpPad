using System;

namespace SharpPad.Dto
{
    /// <summary>
    /// 聊天完成请求的数据传输对象
    /// </summary>
    public class ChatCompletionRequest
    {
        /// <summary>
        /// API请求的URL
        /// </summary>
        public string ChatEndpoint { get; set; }

        /// <summary>
        /// 请求的JSON内容
        /// </summary>
        public string RequestBody { get; set; }
    }
} 