// Monaco Editor 配置
const monacoConfig = {
    paths: {
        'vs': 'monaco-editor/min/vs'
    }
};

// 动态加载 Monaco Editor
function loadMonaco() {
    return new Promise((resolve) => {
        const script = document.createElement('script');
        script.src = 'monaco-editor/min/vs/loader.js';
        script.onload = () => {
            require.config(monacoConfig);
            require(['vs/editor/editor.main'], () => {
                resolve();
            });
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
import { FileListResizer } from './fileSystem/fileListResizer.js';
import { sendRequest } from './utils/apiService.js';
import { showNotification } from './utils/common.js';

// 初始化应用
async function initializeApp() {
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

    // NuGet 相关的全局函数
    window.GetCurrentFiles = () => JSON.parse(localStorage.getItem('controllerFiles') || '[]');
    window.sendRequest = sendRequest;  // 暴露 sendRequest 到全局作用域

    window.loadNuGetConfig = async (file) => {
        try {
            // Load package references
            const packagesDiv = document.getElementById('packageReferences');
            const config = file.nugetConfig || {
                packages: []
            };

            // Display packages
            packagesDiv.innerHTML = config.packages.length === 0 ?
                '<div class="no-packages-message">暂无包引用</div>' :
                config.packages.map(pkg => `
                    <div class="package-reference">
                        <div class="package-reference-info">
                            <span class="package-reference-name">${pkg.id}</span>
                            <span class="package-reference-version">${pkg.version}</span>
                        </div>
                        <button class="remove-button" onclick="removePackageReference('${pkg.id}')">移除</button>
                    </div>
                `).join('');

        } catch (error) {
            console.error('Load NuGet config error:', error);
            showNotification('加载 NuGet 配置失败', 'error');
        }
    };

    window.removePackageReference = async (packageId) => {
        try {
            const file = window.getCurrentFile();
            if (!file || !file.nugetConfig) return;

            // 移除包引用
            file.nugetConfig.packages = file.nugetConfig.packages.filter(p => p.id !== packageId);

            const files = window.GetCurrentFiles();

            // 递归更新文件列表中的文件
            function updateFile(items) {
                for (let item of items) {
                    if (item.id === file.id) {
                        item.nugetConfig = file.nugetConfig;
                        return true;
                    }
                    if (item.type === 'folder' && item.files) {
                        if (updateFile(item.files)) return true;
                    }
                }
                return false;
            }

            updateFile(files);

            // 保存到 localStorage
            localStorage.setItem('controllerFiles', JSON.stringify(files));

            // 重新加载配置
            window.loadNuGetConfig(file);

            showNotification(`包引用 ${packageId} 已移除`, 'success');
        } catch (error) {
            console.error('Remove package reference error:', error);
            showNotification(`移除包引用 ${packageId} 失败: ${error.message}`, 'error');
        }
    };

    window.saveNuGetConfig = async () => {
        try {
            const file = window.getCurrentFile();
            if (!file) return;

            // Get form values
            const config = {
                targetFramework: document.getElementById('targetFramework').value,
                packageSource: document.getElementById('packageSource').value,
                packages: file.nugetConfig?.packages || []
            };

            // Save configuration
            file.nugetConfig = config;

            // 从 localStorage 获取最新的文件列表
            const files = window.GetCurrentFiles();

            // 递归更新文件列表中的文件
            function updateFile(items) {
                for (let item of items) {
                    if (item.id === file.id) {
                        item.nugetConfig = config;
                        return true;
                    }
                    if (item.type === 'folder' && item.files) {
                        if (updateFile(item.files)) return true;
                    }
                }
                return false;
            }

            updateFile(files);

            // Save to localStorage
            localStorage.setItem('controllerFiles', JSON.stringify(files));

            showNotification('NuGet 配置保存成功', 'success');
            window.closeNuGetConfigDialog();

        } catch (error) {
            console.error('Save NuGet config error:', error);
            showNotification('保存 NuGet 配置失败', 'error');
        }
    };

    window.closeNuGetConfigDialog = () => {
        const dialog = document.getElementById('nugetConfigDialog');
        dialog.style.display = 'none';
        window.currentFileId = null;
    };

    window.getCurrentFile = () => {
        const fileId = window.currentFileId;
        if (!fileId) return null;

        const files = window.GetCurrentFiles();

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
    };

    window.addPackageReference = async () => {
        // 从 localStorage 获取文件列表
        const file = window.getCurrentFile();
        if (!file) return;

        // Initialize nugetConfig if it doesn't exist
        if (!file.nugetConfig) {
            file.nugetConfig = {
                packages: []
            };
        }

        // 创建并显示自定义对话框
        const dialog = document.createElement('div');
        dialog.id = 'packageDialog';
        dialog.className = 'modal';
        dialog.style.display = 'block';
        dialog.style.zIndex = '9999';

        // 获取NuGet配置窗口的位置
        const nugetDialog = document.getElementById('nugetConfigDialog');
        const nugetRect = nugetDialog.getBoundingClientRect();

        dialog.innerHTML = `
            <div class="modal-content" style="position: fixed; top: ${nugetRect.top - 20}px; left: 50%; transform: translateX(-50%); width: 400px; background: #2d2d2d; border-radius: 8px; box-shadow: 0 4px 12px rgba(0,0,0,0.3);">
                <div class="modal-header" style="padding: 16px; border-bottom: 1px solid #404040;">
                    <h2 style="margin: 0; color: #e0e0e0; font-size: 18px;">添加 NuGet 包引用</h2>
                </div>
                <div class="modal-body" style="padding: 20px;">
                    <div class="nuget-config-section">
                        <div class="form-group" style="margin-bottom: 16px;">
                            <label for="packageName" style="display: block; margin-bottom: 8px; color: #e0e0e0;">包名:</label>
                            <input type="text" id="packageName" class="form-control" placeholder="例如：Newtonsoft.Json" 
                                style="width: 100%; padding: 8px; background: #3d3d3d; border: 1px solid #505050; border-radius: 4px; color: #e0e0e0;">
                        </div>
                        <div class="form-group" style="margin-bottom: 20px;">
                            <label for="packageVersion" style="display: block; margin-bottom: 8px; color: #e0e0e0;">版本:</label>
                            <input type="text" id="packageVersion" class="form-control" placeholder="例如：13.0.1"
                                style="width: 100%; padding: 8px; background: #3d3d3d; border: 1px solid #505050; border-radius: 4px; color: #e0e0e0;">
                        </div>
                        <div class="button-group" style="display: flex; justify-content: flex-end; gap: 10px;">
                            <button id="cancelButton" class="secondary-button" 
                                style="padding: 8px 16px; border-radius: 4px; border: 1px solid #505050; background: #3d3d3d; color: #e0e0e0; cursor: pointer;">取消</button>
                            <button id="confirmButton" class="primary-button"
                                style="padding: 8px 16px; border-radius: 4px; border: none; background: #0078d4; color: white; cursor: pointer;">确认</button>
                        </div>
                    </div>
                </div>
            </div>
        `;

        document.body.appendChild(dialog);

        // 返回一个 Promise 来处理用户输入
        const getUserInput = () => {
            return new Promise((resolve, reject) => {
                const confirmButton = document.getElementById('confirmButton');
                const cancelButton = document.getElementById('cancelButton');
                const packageNameInput = document.getElementById('packageName');
                const packageVersionInput = document.getElementById('packageVersion');

                confirmButton.addEventListener('click', () => {
                    const name = packageNameInput.value.trim();
                    const version = packageVersionInput.value.trim();
                    if (!name || !version) {
                        showNotification('包名和版本不能为空', 'error');
                        return;
                    }
                    resolve({ name, version });
                    document.body.removeChild(dialog);
                });

                cancelButton.addEventListener('click', () => {
                    reject('用户取消了操作');
                    document.body.removeChild(dialog);
                });
            });
        };

        let userInput;
        try {
            userInput = await getUserInput();
        } catch (error) {
            // 用户取消了操作
            return;
        }

        const { name, version } = userInput;

        // 检查是否已存在相同的包
        if (file.nugetConfig.packages.some(p => p.id.toLowerCase() === name.toLowerCase())) {
            showNotification(`包 ${name} 已存在`, 'error');
            return;
        }

        // 调用后端添加包引用
        const request = {
            Packages: [{ Id: name, Version: version }]
        };

        try {
            const result = await sendRequest('addPackages', request);
            if (result.data.code === 0) {
                showNotification(`已添加包引用: ${name}@${version}`, 'success');
            } else {
                showNotification(`添加包引用失败: ${result.data.message}`, 'error');
                return;
            }
        } catch (err) {
            console.error('添加包引用错误:', err);
            showNotification(`添加包引用失败: ${err.message}`, 'error');
            return;
        }

        // 更新文件的 nugetConfig
        file.nugetConfig.packages.push({ id: name, version: version });

        // 从 localStorage 获取最新的文件列表
        const files = window.GetCurrentFiles();

        // 递归更新文件列表中的文件
        function updateFile(items) {
            for (let item of items) {
                if (item.id === file.id) {
                    item.nugetConfig = file.nugetConfig;
                    return true;
                }
                if (item.type === 'folder' && item.files) {
                    if (updateFile(item.files)) return true;
                }
            }
            return false;
        }

        updateFile(files);

        // 保存更新后的文件列表
        localStorage.setItem('controllerFiles', JSON.stringify(files));

        // 重新加载 NuGet 配置显示
        if (typeof window.loadNuGetConfig === 'function') {
            window.loadNuGetConfig(file);
        }
    };

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

    // 等待 Monaco Editor 加载完成
    await loadMonaco();

    // 注册C#语言支持
    registerCsharpProvider();

    // 初始化编辑器
    const editorInstance = new Editor();
    const editor = editorInstance.initialize('container');

    // 注册编辑器命令
    const commands = new EditorCommands(editor);
    commands.registerCommands();

    // 初始化代码运行器和输出面板
    const outputPanel = new OutputPanel();
    const codeRunner = new CodeRunner();

    // 将编辑器实例暴露给全局，以便其他模块使用
    window.editor = editor;
}

// 启动应用
initializeApp();