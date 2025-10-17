// Monaco Editor 配置
const monacoConfig = {
    paths: {
        'vs': './monaco-editor/min/vs'
    }
};

// 系统设置配置
const systemSettings = {
    // 禁用GPT补全
    disableGptComplete: false
};

// 获取系统设置
export function getSystemSettings() {
    return systemSettings;
}

// 动态加载 Monaco Editor
function loadMonaco() {
    return new Promise((resolve, reject) => {
        // 如果Monaco已经加载，直接返回
        if (typeof monaco !== 'undefined') {
            resolve();
            return;
        }

        const script = document.createElement('script');
        script.src = './monaco-editor/min/vs/loader.js';
        
        script.onload = () => {
            try {
                require.config(monacoConfig);
                require(['vs/editor/editor.main'], () => {
                    // 确保Monaco完全就绪
                    if (typeof monaco !== 'undefined') {
                        resolve();
                    } else {
                        reject(new Error('Monaco Editor failed to load properly'));
                    }
                });
            } catch (error) {
                reject(error);
            }
        };
        
        script.onerror = () => {
            reject(new Error('Failed to load Monaco Editor loader script'));
        };
        
        document.head.appendChild(script);
    });
}

import { Editor } from './editor/editor.js';
import { EditorCommands } from './editor/commands.js';
import { FileManager } from './fileSystem/fileManager.js';
import { registerCsharpProvider } from './csharpLanguageProvider.js';
import { CodeRunner } from './execution/runner.js';
import { OutputPanel } from './execution/outputPanel.js';
import { sendRequest } from './utils/apiService.js';
import { showNotification } from './utils/common.js';
import { NugetManager } from './components/nuget/nugetManager.js';
import { setupSemanticColoring } from './semanticColoring.js';
import desktopBridge from './utils/desktopBridge.js';

// 初始化应用
async function initializeApp() {
    // 初始化系统设置
    loadSystemSettings();

    if (desktopBridge.isAvailable) {
        desktopBridge.onceHostReady(() => {
            console.log('Desktop bridge ready');
            desktopBridge.send({ type: 'ping' });

            // Setup GitHub link to open in system browser
            setupGitHubLinkHandler();
        });

        desktopBridge.onMessage(message => {
            if (!message?.type) {
                return;
            }

            switch (message.type) {
                case 'bridge-error':
                    if (message.message) {
                        showNotification(`桌面通信错误: ${message.message}`, 'error');
                    }
                    console.error('Desktop bridge error:', message);
                    break;
                case 'bridge-warning':
                    console.warn('Desktop bridge warning:', message.message);
                    break;
                case 'pick-and-upload-progress':
                    console.log('Desktop upload in progress...', message.status);
                    break;
                case 'pick-and-upload-completed': {
                    const handled = typeof window.fileManager?.handleDesktopUpload === 'function'
                        ? window.fileManager.handleDesktopUpload(message)
                        : false;

                    if (!handled) {
                        if (message.success) {
                            showNotification('文件上传成功', 'success');
                        } else if (message.cancelled) {
                            showNotification('已取消上传', 'info');
                        } else {
                            const error = message.message || '上传失败，请重试';
                            showNotification(error, 'error');
                        }
                    }
                    break;
                }
                case 'download-file-completed': {
                    const handled = typeof window.fileManager?.handleDesktopDownload === 'function'
                        ? window.fileManager.handleDesktopDownload(message)
                        : false;

                    if (!handled) {
                        if (message.success) {
                            showNotification('文件导出成功', 'success');
                        } else if (message.cancelled) {
                            showNotification('已取消导出', 'info');
                        } else {
                            const error = message.message || '导出失败，请重试';
                            showNotification(error, 'error');
                        }
                    }
                    break;
                }
                case 'pong':
                    console.log('Desktop bridge handshake completed.');
                    break;
                default:
                    break;
            }
        });

        window.requestDesktopUpload = (endpoint, context) => desktopBridge.requestPickAndUpload(endpoint, context);
    } else {
        console.log('Desktop bridge unavailable - running in browser mode.');
    }
    
    // 初始化文件系统
    const fileManager = new FileManager();
    window.fileManager = fileManager; // 暴露到全局作用域
    // 暴露必要的方法到全局作用域
    window.moveItem = (itemId, direction) => fileManager.moveItem(itemId, direction);
    window.renameFile = () => fileManager.renameFile();
    window.deleteFile = () => fileManager.deleteFile();
    window.renameFolder = () => fileManager.renameFolder();
    window.deleteFolder = () => fileManager.deleteFolder();
    window.addFile = () => fileManager.addFile();
    window.addFolder = () => fileManager.addFolder();
    window.duplicateFile = () => fileManager.duplicateFile();
    window.duplicateFolder = () => fileManager.duplicateFolder();
    window.moveOutOfFolder = () => fileManager.moveOutOfFolder();
    window.configureNuGet = () => fileManager.configureNuGet();
    window.addFileToFolder = (folderId) => fileManager.addFileToFolder(folderId);
    window.addFolderToFolder = (parentFolderId) => fileManager.addFolderToFolder(parentFolderId);
    window.exportFolder = () => fileManager.exportFolder();
    window.importFolder = () => fileManager.importFolder();
    window.showOnlyCurrentFolder = (folderId) => fileManager.showOnlyCurrentFolder(folderId);

    // NuGet 相关的全局函数
    window.GetCurrentFiles = () => JSON.parse(localStorage.getItem('controllerFiles') || '[]');
    window.sendRequest = sendRequest;  // 暴露 sendRequest 到全局作用域

    const nugetManager = new NugetManager({ sendRequest, notify: showNotification });
    await nugetManager.initialize();
    window.nugetManager = nugetManager;
    window.closeNuGetConfigDialog = () => nugetManager.close();
    window.loadNuGetConfig = (file) => nugetManager.open(file);

    window.copyCode = function copyCode(button) {
        // 获取代码块内容
        const codeBlock = button.previousElementSibling;

        // 创建临时元素来获取纯文本
        const tempDiv = document.createElement('div');
        tempDiv.innerHTML = codeBlock.innerHTML;
        // 移除语言标签和复制按钮
        const langLabel = tempDiv.querySelector('.lang-label');
        if (langLabel) langLabel.remove();
        const copyBtn = tempDiv.querySelector('.copy-button');
        if (copyBtn) copyBtn.remove();
        // 获取处理后的纯代码文本
        const code = tempDiv.textContent.trim();

        navigator.clipboard.writeText(code).then(() => {
            const originalText = button.textContent;
            button.textContent = '已复制!';
            setTimeout(() => {
                button.textContent = originalText;
            }, 2000);
        }).catch(err => {
            console.error('复制失败:', err);
        });
    };

    fileManager.loadFileList();

    // 系统设置相关的全局函数
    window.closeSystemSettings = () => {
        document.getElementById('systemSettingsModal').style.display = 'none';
    };
    
    window.saveSystemSettings = () => {
        const disableGptComplete = document.getElementById('disableGptComplete').checked;
        
        // 检查是否有设置变化
        const settingsChanged = systemSettings.disableGptComplete !== disableGptComplete;   
        
        // 保存设置到全局对象
        systemSettings.disableGptComplete = disableGptComplete;     
        
        // 保存到localStorage
        localStorage.setItem('systemSettings', JSON.stringify(systemSettings));

        // 显示通知
        showNotification('系统设置已保存', 'success');

        // 关闭对话框
        window.closeSystemSettings();
    };

    // 等待 Monaco Editor 加载完成
    try {
        await loadMonaco();
        console.log('Monaco Editor loaded successfully');
    } catch (error) {
        console.error('Failed to load Monaco Editor:', error);
        showNotification('编辑器加载失败，请刷新页面重试', 'error');
        return;
    }

    // 注册C#语言支持
    try {
        registerCsharpProvider();
        console.log('C# language provider registered');
    } catch (error) {
        console.error('Failed to register C# provider:', error);
    }
    
    // 设置语义着色
    try {
        setupSemanticColoring();
        console.log('Semantic coloring setup completed');
    } catch (error) {
        console.error('Failed to setup semantic coloring:', error);
    }

    // 在Mac上添加额外延迟确保DOM渲染完成
    if (navigator.userAgent.includes('Mac')) {
        await new Promise(resolve => setTimeout(resolve, 50));
    }

    // 初始化编辑器
    let editorInstance, editor;
    try {
        editorInstance = new Editor();
        editor = editorInstance.initialize('container');
        console.log('Editor initialized successfully');
    } catch (error) {
        console.error('Failed to initialize editor:', error);
        showNotification('编辑器初始化失败', 'error');
        return;
    }

    // 注册编辑器命令
    const commands = new EditorCommands(editor);
    commands.registerCommands();

    // 初始化代码运行器和输出面板
    const outputPanel = new OutputPanel();
    window.outputPanelInstance = outputPanel; // 暴露到全局作用域
    const codeRunner = new CodeRunner();
    
    // 页面加载完成后触发输出面板高度调整事件，确保文件列表高度正确
    setTimeout(() => {
        const outputPanelElement = document.getElementById('outputPanel');
        if (outputPanelElement) {
            // 获取当前输出面板高度
            const currentHeight = parseInt(getComputedStyle(outputPanelElement).height, 10);
            // 触发高度更新逻辑
            outputPanel.updateContainerMargins(currentHeight, false);
        }
    }, 100);

    // 系统设置按钮事件
    document.getElementById('systemSettingsBtn').addEventListener('click', () => {
        // 打开之前先更新UI
        document.getElementById('disableGptComplete').checked = systemSettings.disableGptComplete;
        document.getElementById('systemSettingsModal').style.display = 'block';
    });

    // 将编辑器实例暴露给全局，以便其他模块使用
    window.editor = editor;
    window.editorInstance = editorInstance;
    // 暴露 CodeActionProvider 用于调试
    window.editor.codeActionProvider = editorInstance.codeActionProvider;
}

// 从 localStorage 加载系统设置
function loadSystemSettings() {
    try {
        const savedSettings = localStorage.getItem('systemSettings');
        if (savedSettings) {
            const parsed = JSON.parse(savedSettings);
            Object.assign(systemSettings, parsed);
        }
    } catch (error) {
        console.error('加载系统设置出错:', error);
    }
}

// 设置GitHub链接在桌面应用中使用系统浏览器打开
function setupGitHubLinkHandler() {
    const githubLink = document.getElementById('githubLink');
    if (!githubLink) {
        return;
    }

    githubLink.addEventListener('click', (event) => {
        event.preventDefault();
        const url = githubLink.getAttribute('href');
        if (url && desktopBridge.isAvailable) {
            desktopBridge.openExternalUrl(url);
        }
    });
}

// 确保在DOM完全加载后再启动应用
function ensureDOMReady() {
    return new Promise((resolve) => {
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', resolve);
        } else {
            resolve();
        }
    });
}

// 启动应用
async function startApp() {
    // 等待DOM完全就绪
    await ensureDOMReady();
    
    // 在Mac WebView中额外等待一小段时间确保渲染就绪
    if (navigator.userAgent.includes('Mac')) {
        await new Promise(resolve => setTimeout(resolve, 100));
    }
    
    await initializeApp();
}

startApp();
