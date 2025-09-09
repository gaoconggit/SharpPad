// 公共工具函数
export function getCurrentFile() {
    // 获取当前选中的文件
    const selectedFile = document.querySelector('.selected[data-file-id]');
    if (!selectedFile) return null;

    // 获取文件ID
    const fileId = selectedFile.getAttribute('data-file-id');

    // 从 localStorage 获取文件列表
    const filesData = localStorage.getItem('controllerFiles');
    const files = filesData ? JSON.parse(filesData) : [];

    // 递归查找目标文件
    function findFile(items) {
        for (let item of items) {
            if (item.id === fileId) {
                return item;
            }
            if (item.type === 'folder' && item.files) {
                const found = findFile(item.files);
                if (found) return found;
            }
        }
        return null;
    }

    return findFile(files);
}

export function layoutEditor() {
    if (window.editor) {
        setTimeout(() => {
            window.editor.layout();
        }, 200);
    }
}

// 检测设备类型 - 改进逻辑，避免macOS笔记本误判
export function isMobileDevice() {
    // 使用多种方法来检测移动设备
    const userAgent = navigator.userAgent || navigator.vendor || window.opera;
    const mobileRegex = /Android|webOS|iPhone|iPad|iPod|BlackBerry|IEMobile|Opera Mini/i;
    const touchPoints = navigator.maxTouchPoints || 0;
    
    // 优先检查User Agent，避免依赖屏幕尺寸
    if (mobileRegex.test(userAgent)) {
        return true;
    }
    
    // 检查是否有粗糙指针（触摸屏）且屏幕较小
    const hasCoarsePointer = window.matchMedia('(pointer: coarse)').matches;
    const isSmallScreen = window.innerWidth < 768;
    const isVerySmallScreen = window.innerWidth < 480;
    
    // 只有同时满足触摸屏和小屏幕，或非常小的屏幕才认为是移动设备
    return (hasCoarsePointer && isSmallScreen) || isVerySmallScreen || touchPoints > 1;
}

// 根据设备类型获取合适的尺寸
export function getResponsiveSize(defaultSize, mobileSize) {
    return isMobileDevice() ? mobileSize : defaultSize;
}

// 响应式设置container宽度
export function setContainerWidth(container, fileListWidth, chatPanelWidth, chatPanelVisible) {
    // 使用媒体查询检测移动设备
    const isMobile = window.matchMedia('(max-width: 768px) and (pointer: coarse), (max-width: 480px)').matches;
    
    if (isMobile) {
        // 移动设备适配
        if (chatPanelVisible) {
            // 移动设备上聊天面板设置为全屏宽度，主容器隐藏或调整到最小
            container.style.width = '100%';
            container.style.marginRight = '0';
            
            // 根据聊天面板的位置决定编辑器的样式
            // 如果聊天面板在屏幕下半部分
            container.style.height = 'calc(50vh - 1px)';
            container.style.marginBottom = '0';
        } else {
            // 聊天面板不可见时，编辑器占满全屏
            container.style.width = '100%';
            container.style.height = '100vh';
            container.style.marginRight = '0';
            container.style.marginBottom = '0';
        }
        
        // 在移动设备上，文件列表可能会是覆盖式的，而不是并排
        container.style.marginLeft = '0';
    } else {
        // 桌面设备使用原来的布局逻辑
        if (chatPanelVisible) {
            container.style.width = `calc(100% - ${fileListWidth}px - ${chatPanelWidth}px)`;
            container.style.marginRight = `${chatPanelWidth}px`;
        } else {
            container.style.width = `calc(100% - ${fileListWidth}px)`;
            container.style.marginRight = '0';
        }
        container.style.marginLeft = `${fileListWidth}px`;
        container.style.height = '100vh';
    }
    
    // 重新布局编辑器
    layoutEditor();
}

export function showNotification(message, type = 'info') {
    const notification = document.getElementById('notification');
    notification.textContent = message;
    
    // 设置背景颜色
    switch (type) {
        case 'success':
            notification.style.backgroundColor = 'rgba(76, 175, 80, 0.9)';
            break;
        case 'error':
            notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
            break;
        case 'info':
        default:
            notification.style.backgroundColor = 'rgba(33, 150, 243, 0.9)';
            break;
    }
    
    notification.style.display = 'block';
    setTimeout(() => {
        notification.style.display = 'none';
    }, 2000);
}

//定义常量
export const DEFAULT_CODE = `using System;
using System.Threading.Tasks;

class Program
{
    public static async Task Main()
    {
        string message = "Hello, SharpPad! 关注我: https://github.com/gaoconggit/SharpPad";
        foreach (char c in message)
        {
            Console.Write(c);
            await Task.Delay(100);
        }
        Console.WriteLine();
    }
}`;

// 定义常量：completion prompt for code suggestions
export const GPT_COMPLETION_SYSTEM_PROMPT = `## Task: Code Completion
                    
### {{language}}

### Instructions:
- You are a world class coding assistant.
- Given the current text, context, and the last character of the user input, provide a suggestion for code completion.
- The suggestion must be based on the current text, as well as the text before the cursor.
- This is not a conversation, so please do not ask questions or prompt for additional information.

### Notes:
- NEVER INCLUDE ANY MARKDOWN IN THE RESPONSE - THIS MEANS CODEBLOCKS AS WELL.
- Never include any annotations such as "# Suggestion:" or "# Suggestions:".
- Newlines should be included after any of the following characters: "{", "[", "(", ")", "]", "}", and ",".
- Never suggest a newline after a space or newline.
- Ensure that newline suggestions follow the same indentation as the current line.
- The suggestion must start with the last character of the current user input.
- Only ever return the code snippet, do not return any markdown unless it is part of the code snippet.
- Do not return any code that is already present in the current text.
- Do not return anything that is not valid code.
- If you do not have a suggestion, return an empty string.`;

// Start of Selection
export function getSelectedModel() {
    const modelSelect = document.getElementById('modelSelect');
    if (!modelSelect) {
        throw new Error('模型选择器未找到');
    }
    const selectedModel = modelSelect.value;
    const modelConfigs = JSON.parse(localStorage.getItem('chatModels') || '[]');
    const modelConfig = modelConfigs.find(model => model.id === selectedModel);
    if (!modelConfig) {
        throw new Error('请先配置模型');
    }
    return modelConfig;
}
// End of Selection
