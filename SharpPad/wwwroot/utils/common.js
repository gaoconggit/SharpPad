import { extractDirectiveReferences, buildMultiFileContext } from './multiFileHelper.js';

export const PROJECT_TYPE_CHANGE_EVENT = 'sharppad:projectTypeChanged';

export function getCurrentProjectType() {
    const fallback = 'console';
    try {
        const currentFile = getCurrentFile();
        if (currentFile && typeof currentFile.projectType === 'string' && currentFile.projectType.trim()) {
            return currentFile.projectType.trim().toLowerCase();
        }

        const select = document.getElementById('projectTypeSelect');
        if (select && select.value) {
            return select.value.toLowerCase();
        }

        if (typeof window !== 'undefined' && window.localStorage) {
            const stored = window.localStorage.getItem('sharpPad.projectType');
            if (stored) {
                return stored.toLowerCase();
            }
        }
    } catch (error) {
        console.warn('无法获取项目类型:', error);
    }

    return fallback;
}

function sanitizePackageList(packages) {
    if (!Array.isArray(packages)) {
        return [];
    }

    return packages
        .filter(pkg => pkg && typeof pkg === 'object')
        .map(pkg => {
            const id = typeof pkg.id === 'string'
                ? pkg.id.trim()
                : typeof pkg.Id === 'string'
                    ? pkg.Id.trim()
                    : '';

            if (!id) {
                return null;
            }

            const version = typeof pkg.version === 'string'
                ? pkg.version.trim()
                : typeof pkg.Version === 'string'
                    ? pkg.Version.trim()
                    : '';

            return { id, version };
        })
        .filter(Boolean);
}

function mergePackageLists(...groups) {
    const map = new Map();

    for (const group of groups) {
        for (const pkg of sanitizePackageList(group)) {
            const key = pkg.id.toLowerCase();
            if (!map.has(key)) {
                map.set(key, { id: pkg.id, version: pkg.version || '' });
                continue;
            }

            const existing = map.get(key);
            if (!existing.version && pkg.version) {
                map.set(key, { id: pkg.id, version: pkg.version });
            }
        }
    }

    return Array.from(map.values());
}

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

// 判断是否应该使用多文件模式
export function shouldUseMultiFileMode(currentContent) {
    const content = typeof currentContent === 'string' ? currentContent : window.editor?.getValue() || '';
    const references = extractDirectiveReferences(content);
    return references.length > 0;
}

// 创建多文件请求
export function createMultiFileRequest(targetFileName, position, packages, currentContent, projectType) {
    const sanitizedPackages = sanitizePackageList(packages);
    const context = buildMultiFileContext({
        entryFileName: targetFileName || null,
        entryContent: typeof currentContent === 'string' ? currentContent : '',
        entryPackages: sanitizedPackages
    });

    if (!context || !Array.isArray(context.files) || context.files.length === 0) {
        return null;
    }

    const files = context.files.map(file => ({
        FileName: file.name,
        Content: file.content
    }));

    const aggregatedPackages = mergePackageLists(sanitizedPackages, context.packages);

    const request = {
        Files: files,
        TargetFileId: targetFileName || files[0]?.FileName || null,
        Packages: aggregatedPackages.map(pkg => ({
            Id: pkg.id,
            Version: pkg.version
        }))
    };

    if (projectType) {
        request.ProjectType = projectType.toLowerCase();
    }

    if (typeof position === 'number') {
        request.Position = position;
    }

    return request;
}

// 创建单文件请求（向后兼容）
export function createSingleFileRequest(code, position, packages, projectType) {
    return {
        Code: code,
        Position: position,
        Packages: packages,
        ProjectType: (projectType || 'console').toLowerCase()
    };
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
export const GPT_COMPLETION_SYSTEM_PROMPT = `## Task: {{language}} Code Completion Assistant

You are an expert {{language}} coding assistant. Analyze the cursor position and provide a single, contextually appropriate code completion.

### Smart Context Analysis:
- **Method Body**: Complete with executable statements (assignments, method calls, control flow)
- **Class Level**: Complete with member declarations (methods, properties, fields)
- **Empty Lines**: Suggest logical next statements based on code flow above
- **After Keywords**: Complete syntax patterns (if/for/while/using statements)
- **Expression Context**: Complete expressions, method calls, or variable references

### Intelligence Rules:
1. Only suggest completions that make logical sense in the current context
2. Analyze indentation to understand scope and structure
3. Consider variable names and types from surrounding code
4. Follow established patterns and naming conventions in the file
5. Prioritize practical, commonly-used code patterns

### Output Format:
- Return EXACTLY the code to insert (no extra text)
- Match current indentation level precisely
- NO comments unless functionally required
- NO markdown or formatting
- NO explanations
- Empty string if no logical completion exists

### Quality Standards:
- Must be syntactically correct {{language}} code
- Must integrate seamlessly with existing code
- Should improve code flow and readability
- Avoid redundant or obvious completions`;

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

