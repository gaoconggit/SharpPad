import { getCurrentFile, PROJECT_TYPE_CHANGE_EVENT } from '../utils/common.js';
import { sendRequest } from '../utils/apiService.js';
import { FileManager } from '../fileSystem/fileManager.js';
import { buildMultiFileContext } from '../utils/multiFileHelper.js';
import desktopBridge from '../utils/desktopBridge.js';

const fileManager = new FileManager();

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

export class CodeRunner {
    constructor() {
        this.runButton = document.getElementById('runButton');
        this.stopButton = document.getElementById('stopButton');
        this.buildExeButton = document.getElementById('buildExeButton');
        this.outputContent = document.getElementById('outputContent');
        this.notification = document.getElementById('notification');
        this.projectTypeSelect = document.getElementById('projectTypeSelect');
        this.projectTypeStorageKey = 'sharpPad.projectType';
        this.currentSessionId = null;
        this.isRunning = false;
        this.isStopping = false;
        this.initializeProjectTypeSelector();
        this.initializeEventListeners();
    }

    initializeEventListeners() {
        this.runButton.addEventListener('click', () => this.runCode(window.editor.getValue()));
        this.stopButton.addEventListener('click', () => this.stopCode());
        this.buildExeButton.addEventListener('click', () => this.buildExe(window.editor.getValue()));
    }


    initializeProjectTypeSelector() {
        if (!this.projectTypeSelect) {
            return;
        }

        const applyInitialSelection = () => {
            const currentFile = getCurrentFile();
            let preferredType = currentFile?.projectType;

            if (!preferredType) {
                try {
                    preferredType = window.localStorage.getItem(this.projectTypeStorageKey);
                } catch (error) {
                    console.warn('无法读取项目类型偏好:', error);
                }
            }

            const normalized = fileManager.normalizeProjectType(preferredType);
            if ((this.projectTypeSelect.value || '').toLowerCase() !== normalized) {
                this.projectTypeSelect.value = normalized;
            }
        };

        applyInitialSelection();

        this.projectTypeSelect.addEventListener('change', () => {
            const selectedType = fileManager.normalizeProjectType(this.projectTypeSelect.value);
            this.projectTypeSelect.value = selectedType;
            try {
                window.localStorage.setItem(this.projectTypeStorageKey, selectedType);
            } catch (error) {
                console.warn('无法保存项目类型偏好:', error);
            }

            const currentFile = getCurrentFile();
            if (currentFile?.id) {
                fileManager.updateFileProjectType(currentFile.id, selectedType);
            }

            window.dispatchEvent(new CustomEvent(PROJECT_TYPE_CHANGE_EVENT, {
                detail: { projectType: selectedType }
            }));
        });
    }

    setRunningState(isRunning) {
        this.isRunning = isRunning;
        if (isRunning) {
            this.runButton.style.display = 'none';
            this.stopButton.style.display = 'inline-block';
            this.stopButton.disabled = false;
        } else {
            this.runButton.style.display = 'inline-block';
            this.stopButton.style.display = 'none';
            this.stopButton.disabled = false;
        }

        // 如果正在停止，禁用停止按钮
        if (this.isStopping) {
            this.stopButton.disabled = true;
            this.stopButton.textContent = '停止中...';
        } else {
            this.stopButton.textContent = '停止';
        }
    }

    async stopCode() {
        if (!this.currentSessionId) {
            this.appendOutput('没有正在运行的代码需要停止', 'error');
            return;
        }

        if (this.isStopping) {
            this.appendOutput('停止操作正在进行中...', 'info');
            return;
        }

        this.isStopping = true;

        try {
            const request = {
                SessionId: this.currentSessionId
            };

            // 添加超时控制，防止长时间等待
            const controller = new AbortController();
            const timeoutId = setTimeout(() => controller.abort(), 5000); // 5秒超时

            const response = await fetch('/api/coderun/stop', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(request),
                signal: controller.signal
            });

            clearTimeout(timeoutId);

            if (!response.ok) {
                throw new Error(`HTTP ${response.status}: ${response.statusText}`);
            }

            const result = await response.json();

            if (result.success) {
                this.appendOutput('✅ 代码执行已停止', 'info');
                this.notification.textContent = '执行已停止';
                this.notification.style.backgroundColor = 'rgba(255, 152, 0, 0.9)';
                this.notification.style.display = 'block';

                // 3秒后隐藏通知
                setTimeout(() => {
                    this.notification.style.display = 'none';
                }, 3000);
            } else {
                this.appendOutput(`停止失败: ${result.message || '未知错误'}`, 'error');
            }

        } catch (error) {
            if (error.name === 'AbortError') {
                this.appendOutput('停止请求超时，但代码执行可能已被终止', 'error');
            } else {
                this.appendOutput('停止代码执行失败: ' + error.message, 'error');
            }
        } finally {
            // 重置状态
            this.isStopping = false;
            this.currentSessionId = null;
            this.setRunningState(false);
            this.outputContent.classList.remove("result-streaming");
        }
    }

    appendOutput(message, type = 'info') {
        const outputLine = document.createElement('div');
        outputLine.className = `output-${type}`;

        // 尝试自动格式化 JSON
        const formattedMessage = this.formatJSON(message);
        if (formattedMessage !== message) {
            // 如果是 JSON，使用 pre 元素来保持格式
            const pre = document.createElement('pre');
            pre.textContent = formattedMessage;
            pre.style.margin = '0';
            pre.style.fontFamily = 'Consolas, monospace';
            pre.style.whiteSpace = 'pre-wrap';
            outputLine.appendChild(pre);
        } else {
            // 检查是否包含 markdown 格式（如代码块、链接等）
            if (this.containsMarkdown(message)) {
                // 如果包含 markdown，使用 markdown-it 渲染
                const md = this.createMarkdownRenderer();
                outputLine.innerHTML = md.render(message);
                outputLine.classList.add('markdown-content');
            } else {
                // 如果不包含 markdown，使用 pre 元素来保持格式
                const pre = document.createElement('pre');
                pre.textContent = message;
                pre.style.margin = '0';
                pre.style.fontFamily = 'Consolas, monospace';
                pre.style.whiteSpace = 'pre-wrap';
                outputLine.appendChild(pre);
            }
        }

        this.outputContent.appendChild(outputLine);
        this.outputContent.scrollTop = this.outputContent.scrollHeight;
    }

    streamOutput(message, type = 'info') {
        this.outputContent.classList.add("result-streaming");
        
        const outputDiv = document.createElement('div');
        outputDiv.className = `output-${type}`;
        
        // 检查是否包含 markdown 格式（如代码块、链接等）
        if (this.containsMarkdown(message)) {
            // 如果包含 markdown，使用 markdown-it 渲染
            const md = this.createMarkdownRenderer();
            outputDiv.innerHTML = md.render(message);
            outputDiv.classList.add('markdown-content');
        } else {
            // 如果不包含 markdown，使用 pre 元素显示输出，保持换行符
            const pre = document.createElement('pre');
            pre.textContent = message;
            pre.style.margin = '0';
            pre.style.fontFamily = 'Consolas, monospace';
            pre.style.whiteSpace = 'pre-wrap';
            pre.style.wordBreak = 'break-word';
            outputDiv.appendChild(pre);
        }
        
        this.outputContent.innerHTML = '';
        this.outputContent.appendChild(outputDiv);
        
        // 滚动到底部
        this.outputContent.scrollTop = this.outputContent.scrollHeight;
    }

    formatJSON(str) {
        try {
            const obj = JSON.parse(str);
            return JSON.stringify(obj, null, 2);
        } catch (e) {
            return str;
        }
    }

    // 检查文本是否包含 markdown 格式

    getProjectTypeLabel(projectType) {
        switch ((projectType || '').toLowerCase()) {
            case 'winforms':
                return 'WinForms 桌面';
            case 'webapi':
                return 'ASP.NET Core Web API';
            case 'avalonia':
                return 'Avalonia 桌面';
            default:
                return 'Console 应用';
        }
    }

    containsMarkdown(text) {
        // 检查常见的 markdown 模式
        const markdownPatterns = [
            /```[\s\S]*?```/,           // 代码块
            /`[^`]+`/,                  // 行内代码
            /^\s*#{1,6}\s+/m,           // 标题
            /\[([^\]]+)\]\([^)]+\)/,    // 链接
            /!\[([^\]]*)\]\([^)]+\)/,   // 图片
            /^\s*[-*+]\s+/m,            // 无序列表
            /^\s*\d+\.\s+/m,            // 有序列表
            /\*\*[^*]+\*\*/,            // 粗体
            /\*[^*]+\*/,                // 斜体
            /~~[^~]+~~/,                // 删除线
            /^\s*>\s+/m,                // 引用
            /^\s*---+\s*$/m,            // 分隔线
            /^\s*\|\s*.+\s*\|/m         // 表格
        ];
        
        return markdownPatterns.some(pattern => pattern.test(text));
    }

    // 创建 markdown 渲染器
    createMarkdownRenderer() {
        // 检查 markdown-it 是否可用
        if (typeof window.markdownit === 'undefined') {
            console.warn('markdown-it 未加载，无法渲染 markdown');
            return null;
        }

        return window.markdownit({
            breaks: true,
            highlight: function (str, lang) {
                if (lang && window.hljs && window.hljs.getLanguage && window.hljs.getLanguage(lang)) {
                    try {
                        const highlighted = window.hljs.highlight(str, { language: lang }).value;
                        return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${highlighted}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                    } catch (_) {
                        return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${window.markdownit().utils.escapeHtml(str)}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                    }
                } else if (window.hljs && window.hljs.highlightAuto) {
                    const detected = window.hljs.highlightAuto(str);
                    const detectedLang = detected.language || 'text';
                    return `<pre class="hljs"><code><div class="lang-label">${detectedLang}</div>${detected.value}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                } else {
                    return `<pre class="hljs"><code>${window.markdownit().utils.escapeHtml(str)}</code></pre>`;
                }
            }
        });
    }

    async runCode(code) {
        if (!code) return;

        //运行代码之前如果已选择文件则自动保存
        const fileId = document.querySelector('#fileListItems a.selected')?.getAttribute('data-file-id');
        if (fileId) {
            // 使用 FileManager 的公共保存方法
            fileManager.saveFileToLocalStorage(fileId, code);
        }

        const file = getCurrentFile();
        const basePackages = file?.nugetConfig?.packages || [];
        const csharpVersion = document.getElementById('csharpVersion')?.value || 2147483647;
        const projectType = fileManager.normalizeProjectType(this.projectTypeSelect?.value);
        this.currentSessionId = 'session_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);
        this.setRunningState(true);

        const { selectedFiles, autoIncludedNames, missingReferences, packages: contextPackages } = this.gatherMultiFileContext(code, fileId, file);
        const combinedPackages = mergePackageLists(basePackages, contextPackages);
        const preRunMessages = [];

        if (autoIncludedNames.length > 0) {
            preRunMessages.push({ type: 'info', text: `自动包含引用文件: ${autoIncludedNames.join(', ')}` });
        }

        if (missingReferences.length > 0) {
            preRunMessages.push({ type: 'error', text: `未找到引用文件: ${missingReferences.join(', ')}` });
        }

        let request;

        if (selectedFiles.length > 0) {
            const filesWithContent = selectedFiles.map(f => ({
                FileName: f.name,
                Content: f.content,
                IsEntry: f.isEntry || f.content.includes('static void Main') || f.content.includes('static Task Main') || f.content.includes('static async Task Main')
            }));

            request = {
                Files: filesWithContent,
                Packages: combinedPackages.map(p => ({
                    Id: p.id,
                    Version: p.version
                })),
                LanguageVersion: parseInt(csharpVersion),
                ProjectType: projectType,
                SessionId: this.currentSessionId
            };
        } else {
            request = {
                SourceCode: code,
                Packages: combinedPackages.map(p => ({
                    Id: p.id,
                    Version: p.version
                })),
                LanguageVersion: parseInt(csharpVersion),
                ProjectType: projectType,
                SessionId: this.currentSessionId
            };
        }

        // 清空输出区域
        this.outputContent.innerHTML = '';
        preRunMessages.forEach(msg => this.appendOutput(msg.text, msg.type));

        try {
            let result = "";
            const { reader, showNotificationTimer } = await sendRequest('run', request);
            while (true) {
                const { value, done } = await reader.read();
                if (done) break;

                // 将 Uint8Array 转换为字符串
                const text = new TextDecoder("utf-8").decode(value);
                // 处理每一行数据
                const lines = text.split('\n');

                for (const line of lines) {
                    if (!line.trim()) continue;
                    if (!line.startsWith('data: ')) continue;

                    const data = JSON.parse(line.substring(6));
                    switch (data.type) {
                        case 'output':
                            // 检查是否是输入请求
                            if (data.content.includes('[INPUT REQUIRED]')) {
                                this.showInputRequest(data.content);
                            } else {
                                result += data.content;
                                this.streamOutput(result, 'success');
                            }
                            break;
                        case 'error':
                            this.appendOutput(data.content, 'error');
                            this.notification.textContent = '运行出错';
                            this.notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
                            this.notification.style.display = 'block';
                            break;
                        case 'completed':
                            clearTimeout(showNotificationTimer); // 清除显示通知的定时器
                            this.notification.style.display = 'none';
                            this.outputContent.classList.remove("result-streaming");
                            this.currentSessionId = null; // 清空会话ID
                            this.setRunningState(false); // 重置运行状态
                            return;
                    }
                }
            }
        } catch (error) {
            this.appendOutput('运行请求失败: ' + error.message, 'error');
            this.notification.textContent = '运行失败';
            this.notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
            this.notification.style.display = 'block';
            this.setRunningState(false); // 重置运行状态
        }
    }

    gatherMultiFileContext(code, fileId, currentFile = null) {
        const entryName = currentFile?.name || getCurrentFile()?.name || null;
        const context = buildMultiFileContext({
            entryFileId: fileId || currentFile?.id || null,
            entryFileName: entryName,
            entryContent: code,
            entryPackages: currentFile?.nugetConfig?.packages || []
        });

        const selectedFiles = Array.isArray(context.files) ? context.files.map(file => ({
            id: file.id,
            name: file.name,
            content: file.content,
            isEntry: !!file.isEntry,
            packages: Array.isArray(file.packages) ? file.packages : []
        })) : [];

        return {
            selectedFiles,
            autoIncludedNames: Array.isArray(context.autoIncludedNames) ? context.autoIncludedNames : [],
            missingReferences: Array.isArray(context.missingReferences) ? context.missingReferences : [],
            packages: Array.isArray(context.packages) ? context.packages : []
        };
    }

    // 新增的方法，使用 FileManager 的公共保存方法
    async saveCurrentFile(code) {
        try {
            const currentFile = getCurrentFile();
            if (currentFile) {
                fileManager.saveFileToLocalStorage(currentFile.id, code);
                return true;
            }
            return false;
        } catch (error) {
            console.error('保存文件时出错:', error);
            return false;
        }
    }

    showInputRequest(message) {
        // 移除已存在的输入框
        const existingInputs = this.outputContent.querySelectorAll('.input-container');
        existingInputs.forEach(input => input.remove());
        
        // 显示输入请求消息
        this.appendOutput(message, 'info');
        
        // 创建输入框
        const inputContainer = document.createElement('div');
        inputContainer.className = 'input-container';
        inputContainer.style.cssText = `
            display: flex;
            margin: 10px 0;
            padding: 10px;
            background: #f8f8f8;
            border-radius: 4px;
            border: 1px solid #ddd;
        `;

        const inputField = document.createElement('input');
        inputField.type = 'text';
        inputField.placeholder = '请输入数据...';
        inputField.style.cssText = `
            flex: 1;
            padding: 5px 10px;
            border: 1px solid #ccc;
            border-radius: 3px;
            font-family: Consolas, monospace;
        `;

        const sendButton = document.createElement('button');
        sendButton.textContent = '发送';
        sendButton.style.cssText = `
            margin-left: 10px;
            padding: 5px 15px;
            background: #007acc;
            color: white;
            border: none;
            border-radius: 3px;
            cursor: pointer;
        `;

        // 发送输入事件
        const sendInput = async () => {
            const input = inputField.value;
            if (input.trim() === '') {
                inputField.focus();
                return;
            }
            
            // 显示用户输入
            this.appendOutput(`> ${input}`, 'input');
            
            // 发送到后端
            await this.sendInput(input);
            
            // 移除输入框
            inputContainer.remove();
        };

        sendButton.addEventListener('click', sendInput);
        inputField.addEventListener('keypress', (e) => {
            if (e.key === 'Enter') {
                sendInput();
            }
        });

        inputContainer.appendChild(inputField);
        inputContainer.appendChild(sendButton);
        this.outputContent.appendChild(inputContainer);

        // 自动聚焦输入框
        inputField.focus();
        this.outputContent.scrollTop = this.outputContent.scrollHeight;
    }

    async sendInput(input) {
        if (!this.currentSessionId) {
            this.appendOutput('错误：没有活动的执行会话', 'error');
            return;
        }

        try {
            const request = {
                SessionId: this.currentSessionId,
                Input: input
            };

            const response = await fetch('/api/coderun/input', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(request)
            });

            const result = await response.json();
            
            if (!result.success) {
                this.appendOutput('发送输入失败', 'error');
            }
            // 用户输入已经在 sendInput 函数中显示了，这里不需要重复显示
        } catch (error) {
            this.appendOutput('发送输入时出错: ' + error.message, 'error');
        }
    }

    async buildExe(code) {
        if (!code) {
            this.appendOutput('没有代码可以构建', 'error');
            return;
        }

        // 保存当前文件
        const fileId = document.querySelector('#fileListItems a.selected')?.getAttribute('data-file-id');
        if (fileId) {
            fileManager.saveFileToLocalStorage(fileId, code);
        }

        const file = getCurrentFile();
        const basePackages = file?.nugetConfig?.packages || [];
        const csharpVersion = document.getElementById('csharpVersion')?.value || 2147483647;

        let request;
        let outputFileName = 'Program.exe';

        const projectType = fileManager.normalizeProjectType(this.projectTypeSelect?.value);

        const { selectedFiles, autoIncludedNames, missingReferences, packages: contextPackages } = this.gatherMultiFileContext(code, fileId, file);
        const combinedPackages = mergePackageLists(basePackages, contextPackages);
        const preBuildMessages = [];

        if (autoIncludedNames.length > 0) {
            preBuildMessages.push({ type: 'info', text: `自动包含引用文件: ${autoIncludedNames.join(', ')}` });
        }

        if (missingReferences.length > 0) {
            preBuildMessages.push({ type: 'error', text: `未找到引用文件: ${missingReferences.join(', ')}` });
        }

        if (selectedFiles.length > 0) {
            const filesWithContent = selectedFiles.map(f => ({
                FileName: f.name,
                Content: f.content,
                IsEntry: f.isEntry || f.content.includes('static void Main') || f.content.includes('static Task Main') || f.content.includes('static async Task Main')
            }));

            const mainFile = filesWithContent.find(f => f.IsEntry) || filesWithContent[0];
            if (mainFile) {
                outputFileName = mainFile.FileName.replace('.cs', '.exe');
            }

            request = {
                Files: filesWithContent,
                Packages: combinedPackages.map(p => ({
                    Id: p.id,
                    Version: p.version
                })),
                LanguageVersion: parseInt(csharpVersion),
                OutputFileName: outputFileName,
                ProjectType: projectType
            };

            preBuildMessages.push({ type: 'info', text: `正在构建 ${selectedFiles.length} 个文件为 ${outputFileName}...` });
        } else {
            if (file && file.name && file.name.endsWith('.cs')) {
                outputFileName = file.name.replace('.cs', '.exe');
            }

            request = {
                SourceCode: code,
                Packages: combinedPackages.map(p => ({
                    Id: p.id,
                    Version: p.version
                })),
                LanguageVersion: parseInt(csharpVersion),
                OutputFileName: outputFileName,
                ProjectType: projectType
            };

            preBuildMessages.push({ type: 'info', text: `正在构建 ${outputFileName}...` });
        }

        // 禁用构建按钮防止重复点击
        this.buildExeButton.disabled = true;
        this.buildExeButton.textContent = '构建中...';

        // 清空输出区域
        this.outputContent.innerHTML = '';
        preBuildMessages.forEach(msg => this.appendOutput(msg.text, msg.type));

        try {
            let buildOutput = "";
            const { reader, showNotificationTimer } = await sendRequest('buildExe', request);

            while (true) {
                const { value, done } = await reader.read();
                if (done) break;

                // 将 Uint8Array 转换为字符串
                const text = new TextDecoder("utf-8").decode(value);
                // 处理每一行数据
                const lines = text.split('\n');

                for (const line of lines) {
                    if (!line.trim()) continue;
                    if (!line.startsWith('data: ')) continue;

                    const data = JSON.parse(line.substring(6));
                    switch (data.type) {
                        case 'output':
                            buildOutput += data.content;
                            this.streamOutput(buildOutput, 'info');
                            break;
                        case 'error':
                            this.appendOutput(data.content, 'error');
                            this.notification.textContent = '构建失败';
                            this.notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
                            this.notification.style.display = 'block';
                            break;
                        case 'completed':
                            clearTimeout(showNotificationTimer);
                            this.outputContent.classList.remove("result-streaming");

                            if (data.success && data.downloadId) {
                                const downloadUrl = `/api/coderun/downloadBuild/${data.downloadId}`;
                                const shouldUseDesktop = this.shouldUseDesktopDownload();

                                if (shouldUseDesktop) {
                                    try {
                                        await this.downloadBuildViaDesktop(downloadUrl, data.fileName, data.downloadId);
                                    } catch (downloadError) {
                                        this.appendOutput(`下载发布包失败: ${downloadError.message}`, 'error');
                                        this.notification.textContent = '构建成功但下载失败';
                                        this.notification.style.backgroundColor = 'rgba(255, 152, 0, 0.95)';
                                        this.notification.style.display = 'block';
                                        break;
                                    }
                                } else {
                                    const a = document.createElement('a');
                                    a.href = downloadUrl;
                                    a.download = data.fileName;
                                    document.body.appendChild(a);
                                    a.click();
                                    document.body.removeChild(a);
                                }

                                this.appendOutput(`\n✅ 成功构建 ${data.fileName}，文件已开始下载`, 'success');
                                this.notification.textContent = `构建成功：${data.fileName}`;
                                this.notification.style.backgroundColor = 'rgba(76, 175, 80, 0.9)';
                                this.notification.style.display = 'block';

                                // 3秒后隐藏通知
                                setTimeout(() => {
                                    this.notification.style.display = 'none';
                                }, 3000);
                            }
                            return;
                    }
                }
            }
        } catch (error) {
            this.appendOutput('构建失败: ' + error.message, 'error');
            this.notification.textContent = '构建失败';
            this.notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
            this.notification.style.display = 'block';
        } finally {
            // 重新启用构建按钮
            this.buildExeButton.disabled = false;
            this.buildExeButton.textContent = '发布';
            this.outputContent.classList.remove("result-streaming");
        }
    }

    shouldUseDesktopDownload() {
        if (!desktopBridge?.isAvailable) {
            return false;
        }

        if (typeof fileManager?.shouldUseDesktopExport === 'function') {
            return fileManager.shouldUseDesktopExport();
        }

        return false;
    }

    async downloadBuildViaDesktop(downloadUrl, fileName, downloadId) {
        if (!desktopBridge?.isAvailable) {
            throw new Error('当前环境不支持桌面下载通道。');
        }

        const response = await fetch(downloadUrl, { credentials: 'include' });
        if (!response.ok) {
            throw new Error(`请求下载失败 (HTTP ${response.status})`);
        }

        const buffer = await response.arrayBuffer();
        const base64Content = this.arrayBufferToBase64(buffer);
        const artifactName = typeof fileName === 'string' && fileName.trim().length > 0
            ? fileName.trim()
            : 'SharpPadBuild.zip';
        const mimeType = this.getArtifactMimeType(artifactName);

        const posted = desktopBridge.requestFileDownload({
            fileName: artifactName,
            content: base64Content,
            mimeType,
            isBase64: true,
            context: {
                action: 'build-download',
                downloadId
            }
        });

        if (!posted) {
            throw new Error('无法发起桌面下载请求。');
        }
    }

    arrayBufferToBase64(buffer) {
        const bytes = new Uint8Array(buffer);
        const chunkSize = 0x8000;
        let binary = '';

        for (let i = 0; i < bytes.length; i += chunkSize) {
            const chunk = bytes.subarray(i, i + chunkSize);
            binary += String.fromCharCode.apply(null, chunk);
        }

        return btoa(binary);
    }

    getArtifactMimeType(fileName) {
        if (typeof fileName !== 'string') {
            return 'application/octet-stream';
        }

        const lower = fileName.toLowerCase();
        if (lower.endsWith('.zip')) {
            return 'application/zip';
        }

        if (lower.endsWith('.dll')) {
            return 'application/octet-stream';
        }

        if (lower.endsWith('.exe')) {
            return 'application/octet-stream';
        }

        return 'application/octet-stream';
    }
}

