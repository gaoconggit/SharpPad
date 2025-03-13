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
// End of Selectio
