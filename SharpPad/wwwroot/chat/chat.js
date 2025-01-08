import { layoutEditor, showNotification } from '../utils/common.js';
import { fileListResizer } from '../fileSystem/fileListResizer.js';

export class ChatManager {
    constructor() {
        this.chatPanel = document.getElementById('chatPanel');
        this.container = document.getElementById('container');
        this.toggleChat = document.getElementById('toggleChat');
        this.chatMessages = document.getElementById('chatMessages');
        this.chatInput = document.getElementById('chatInput');
        this.clearChat = document.getElementById('clearChat');
        this.minimizedChatButton = document.querySelector('.minimized-chat-button');
        this.modelSelect = document.getElementById('modelSelect');
        this.modelSettingsBtn = document.getElementById('modelSettingsBtn');
        this.modelSettingsModal = document.getElementById('modelSettingsModal');
        this.isResizing = false;
        this.startX = 0;
        this.startWidth = 0;
        this.isInputResizing = false;
        this.startY = 0;
        this.startHeight = 0;

        this.initializeEventListeners();
        this.loadChatHistory();
        this.initializeModels();
    }

    initializeEventListeners() {
        // 窗口大小变化监听
        window.addEventListener('resize', () => {
            this.handleResize();
            layoutEditor();
        });

        // 聊天面板拖拽调整大小
        this.chatPanel.addEventListener('mousedown', this.handleMouseDown.bind(this));
        document.addEventListener('mousemove', this.handleMouseMove.bind(this));
        document.addEventListener('mouseup', this.handleMouseUp.bind(this));

        // 聊天输入框大小调整
        const resizeHandle = document.querySelector('.chat-input-resize-handle');
        resizeHandle.addEventListener('mousedown', this.handleInputResizeStart.bind(this));
        document.addEventListener('mousemove', this.handleInputResize.bind(this));
        document.addEventListener('mouseup', this.handleInputResizeEnd.bind(this));

        // 聊天输入监听
        this.chatInput.addEventListener('keypress', async (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                await this.sendChatMessage();
            }
        });

        // 添加快捷键清除功能
        document.addEventListener('keydown', (e) => {
            // Alt + L 清除聊天记录
            if (e.altKey && e.key.toLowerCase() === 'l') {
                e.preventDefault();
                this.clearChat.click();
            }
        });

        // 清除聊天记录按钮
        this.clearChat.addEventListener('click', () => {
            if (confirm('确定要清除所有聊天记录吗？此操作不可恢复。')) {
                this.chatMessages.innerHTML = '';
                this.saveChatHistory([]);
                showNotification('聊天记录已清除', 'success');
            }
        });

        // 切换聊天窗口显示/隐藏
        this.toggleChat.addEventListener('click', () => {
            this.chatPanel.style.display = 'none';
            this.minimizedChatButton.style.display = 'block';
            fileListResizer.updateContainerWidth();
        });

        // 恢复聊天窗口
        this.minimizedChatButton.querySelector('.restore-chat').addEventListener('click', () => {
            this.chatPanel.style.display = 'flex';
            this.minimizedChatButton.style.display = 'none';
            fileListResizer.updateContainerWidth();
        });

        //模型设置
        this.modelSettingsBtn.addEventListener('click', () => {
            this.modelSettingsModal.style.display = 'block';
        });

        // 添加模型按钮
        const addModelBtn = document.getElementById('addModelBtn');
        const addModelModal = document.getElementById('addModelModal');
        if (addModelBtn) {
            addModelBtn.addEventListener('click', () => {
                addModelModal.style.display = 'block';
            });
        }

        // 暴露关闭模型设置对话框的方法到全局
        window.closeModelSettings = () => {
            this.modelSettingsModal.style.display = 'none';
        };

        // 暴露关闭添加模型对话框的方法到全局
        window.closeAddModel = () => {
            addModelModal.style.display = 'none';
        };

        // 暴露关闭编辑模型对话框的方法到全局
        window.closeEditModel = () => {
            document.getElementById('editModelModal').style.display = 'none';
        };

        // 暴露编辑模型的方法到全局
        window.editModel = (modelId) => this.editModel(modelId);

        // 暴露保存编辑模型的方法到全局
        window.saveEditModel = () => this.saveEditModel();

        // 暴露保存新模型的方法到全局
        window.saveNewModel = () => this.saveNewModel();

        // 暴露切换API Key可见性的方法到全局
        window.toggleApiKeyVisibility = (button) => {
            const input = button.previousElementSibling;
            if (input.type === 'password') {
                input.type = 'text';
                button.textContent = '🔒';
            } else {
                input.type = 'password';
                button.textContent = '👁';
            }
        };

        // 暴露删除模型的方法到全局
        window.deleteModel = (modelId, showConfirm = true) => this.deleteModel(modelId, showConfirm);
    }

    handleMouseDown(e) {
        const leftEdge = this.chatPanel.getBoundingClientRect().left;
        if (Math.abs(e.clientX - leftEdge) > 10) return;

        this.isResizing = true;
        this.startX = e.clientX;
        this.startWidth = parseInt(document.defaultView.getComputedStyle(this.chatPanel).width, 10);
        this.chatPanel.classList.add('resizing');
        document.documentElement.style.cursor = 'ew-resize';
        e.preventDefault();
    }

    handleMouseMove(e) {
        if (!this.isResizing) return;

        const width = this.startWidth - (e.clientX - this.startX);
        if (width >= 450 && width <= window.innerWidth * 0.6) {
            this.chatPanel.style.width = `${width}px`;
            this.container.style.marginRight = `${width}px`;
            const fileListWidth = parseInt(getComputedStyle(fileList).width, 10);
            this.container.style.width = `calc(100% - ${fileListWidth}px - ${width}px)`;
            layoutEditor();
        }
    }

    handleMouseUp() {
        if (this.isResizing) {
            this.isResizing = false;
            this.chatPanel.classList.remove('resizing');
            document.documentElement.style.cursor = '';
        }
    }

    handleResize() {
        const maxWidth = window.innerWidth * 0.4;
        const currentWidth = parseInt(getComputedStyle(this.chatPanel).width, 10);

        if (currentWidth > maxWidth) {
            const newWidth = maxWidth;
            this.chatPanel.style.width = `${newWidth}px`;
            this.container.style.marginRight = `${newWidth}px`;
            const fileListWidth = parseInt(getComputedStyle(fileList).width, 10);
            this.container.style.width = `calc(100% - ${fileListWidth}px - ${newWidth}px)`;
            layoutEditor();
        }
    }

    loadChatHistory() {
        const history = localStorage.getItem('chatHistory');
        const messages = history ? JSON.parse(history) : [];
        messages.forEach(msg => this.addMessageToChat(msg.role, msg.content));
    }

    saveChatHistory(messages) {
        localStorage.setItem('chatHistory', JSON.stringify(messages));
    }

    addMessageToChat(role, content) {
        const messageDiv = document.createElement('div');
        messageDiv.className = `chat-message ${role}-message`;

        const md = window.markdownit({
            breaks: true,
            highlight: function (str, lang) {
                if (lang && window.hljs.getLanguage(lang)) {
                    try {
                        const highlighted = window.hljs.highlight(str, { language: lang }).value;
                        return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${highlighted}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                    } catch (_) {
                        return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${md.utils.escapeHtml(str)}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                    }
                } else {
                    const detected = window.hljs.highlightAuto(str);
                    const lang = detected.language || 'text';
                    return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${detected.value}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                }
            }
        });
        messageDiv.innerHTML = md.render(content);
        this.chatMessages.appendChild(messageDiv);
        this.chatMessages.scrollTop = this.chatMessages.scrollHeight;
    }

    async sendChatMessage() {
        const message = this.chatInput.value.trim();
        if (!message) return;

        // 获取当前消息历史
        const messages = this.getChatHistory();
        // 添加用户消息
        messages.push({ role: 'user', content: message });
        this.saveChatHistory(messages);
        this.addMessageToChat('user', message);
        this.chatInput.value = '';

        try {
            // 创建一个新的消息div用于流式输出
            const messageDiv = document.createElement('div');
            messageDiv.className = 'chat-message assistant-message';
            const contentDiv = document.createElement('div');
            contentDiv.className = 'result-streaming';
            messageDiv.appendChild(contentDiv);
            this.chatMessages.appendChild(messageDiv);
            this.chatMessages.scrollTop = this.chatMessages.scrollHeight;

            const { reader } = await this.chatToLLM(messages);
            let result = "";

            // 配置 markdown-it
            const md = window.markdownit({
                highlight: this.highlightCode
            });

            // 添加延时函数
            const delay = ms => new Promise(resolve => setTimeout(resolve, ms));
            let lastRenderTime = Date.now();
            const MIN_RENDER_INTERVAL = 1; // 最小渲染间隔（毫秒）
            let buffer = ''; // 用于存储未完成的数据

            while (true) {
                const { value, done } = await reader.read();
                if (done) {
                    break;
                }
                // 将 Uint8Array 转换为字符串
                const text = new TextDecoder("utf-8").decode(value);
                buffer += text;

                // 尝试按行处理数据
                let newlineIndex;
                while ((newlineIndex = buffer.indexOf('\n')) !== -1) {
                    const line = buffer.slice(0, newlineIndex);
                    buffer = buffer.slice(newlineIndex + 1);

                    if (!line.trim() || !line.startsWith('data: ')) continue;
                    if (line === 'data: [DONE]') {
                        contentDiv.classList.remove('result-streaming');
                        // 保存助手回复
                        if (result) {
                            messages.push({ role: 'assistant', content: result });
                            this.saveChatHistory(messages);
                        }
                        break;
                    }

                    try {
                        const data = JSON.parse(line.substring(6));
                        if (data.choices && data.choices[0]) {
                            const delta = data.choices[0].delta;
                            if (delta && delta.content) {
                                result += delta.content;

                                // 检查是否需要延时
                                const now = Date.now();
                                const timeSinceLastRender = now - lastRenderTime;
                                if (timeSinceLastRender < MIN_RENDER_INTERVAL) {
                                    await delay(MIN_RENDER_INTERVAL - timeSinceLastRender);
                                }

                                // 使用配置的markdown-it渲染
                                contentDiv.innerHTML = md.render(result);
                                this.chatMessages.scrollTop = this.chatMessages.scrollHeight;
                                lastRenderTime = Date.now();
                            }
                        } else if (data.error) {
                            contentDiv.innerHTML = `<span class="error">${data.error.message || '发生错误'}</span>`;
                            contentDiv.classList.remove('result-streaming');
                            return;
                        }
                    } catch (err) {
                        console.error('Error parsing SSE message:', err, line);
                        continue;
                    }
                }
            }

        } catch (error) {
            this.addMessageToChat('assistant', '抱歉，发生了错误，请稍后重试。');
            // 保存错误消息
            messages.push({ role: 'assistant', content: '抱歉，发生了错误，请稍后重试。' });
            this.saveChatHistory(messages);
        }
    }

    async chatToLLM(messages) {
        const selectedModel = this.modelSelect.value;
        const modelConfigs = JSON.parse(localStorage.getItem('chatModels') || '[]');
        const modelConfig = modelConfigs.find(m => m.id === selectedModel);

        if (!modelConfig) {
            throw new Error('请先配置模型');
        }

        const reqMessages = modelConfig.systemPrompt
            ? [{ role: 'system', content: modelConfig.systemPrompt }, ...messages]
            : messages;

        const response = await fetch(modelConfig.endpoint, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'Authorization': `Bearer ${modelConfig.apiKey}`,
            },
            body: JSON.stringify({
                model: modelConfig.id,
                messages: reqMessages,
                stream: true
            })
        });

        if (!response.ok) {
            throw new Error(`HTTP error! status: ${response.status}`);
        }

        return {
            reader: response.body.getReader()
        };
    }

    getChatHistory() {
        const history = localStorage.getItem('chatHistory');
        return history ? JSON.parse(history) : [];
    }

    initializeModels() {
        const savedModels = localStorage.getItem('chatModels');
        const models = savedModels ? JSON.parse(savedModels) : [];

        if (!savedModels) {
            localStorage.setItem('chatModels', JSON.stringify(models));
        }

        this.updateModelList();
        this.updateModelSelect();

        // 恢复上次选择的模型
        const lastSelectedModel = localStorage.getItem('lastSelectedModel');
        if (lastSelectedModel && this.modelSelect.querySelector(`option[value="${lastSelectedModel}"]`)) {
            this.modelSelect.value = lastSelectedModel;
        }

        // 监听模型选择变化
        this.modelSelect.addEventListener('change', () => {
            localStorage.setItem('lastSelectedModel', this.modelSelect.value);
        });
    }

    updateModelSelect() {
        const models = JSON.parse(localStorage.getItem('chatModels') || '[]');
        this.modelSelect.innerHTML = '';

        models.forEach(model => {
            const option = document.createElement('option');
            option.value = model.id;
            option.textContent = model.name;
            this.modelSelect.appendChild(option);
        });
    }

    updateModelList() {
        const modelList = document.getElementById('modelList');
        const models = JSON.parse(localStorage.getItem('chatModels') || '[]');

        if (!modelList) return;

        modelList.innerHTML = '';
        models.forEach(model => {
            const modelItem = document.createElement('div');
            modelItem.className = 'model-item';
            modelItem.innerHTML = `
                <div class="model-info">
                    <div class="model-name">${model.name}</div>
                    <div class="model-endpoint">${model.endpoint}</div>
                    <div class="model-api-key">${model.apiKey ? '******' : '未设置API Key'}</div>
                </div>
                <div class="model-actions">
                    <button class="edit-model" data-model-id="${model.id}">编辑</button>
                    <button class="delete-model" data-model-id="${model.id}">删除</button>
                </div>
            `;
            modelList.appendChild(modelItem);
        });

        // 添加事件监听
        modelList.querySelectorAll('.edit-model').forEach(button => {
            button.addEventListener('click', () => this.editModel(button.dataset.modelId));
        });

        modelList.querySelectorAll('.delete-model').forEach(button => {
            button.addEventListener('click', () => this.deleteModel(button.dataset.modelId));
        });
    }

    highlightCode(str, lang) {
        if (lang && window.hljs.getLanguage(lang)) {
            try {
                const highlighted = window.hljs.highlight(str, { language: lang }).value;
                return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${highlighted}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
            } catch (_) {
                return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${window.markdownit().utils.escapeHtml(str)}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
            }
        } else {
            const detected = window.hljs.highlightAuto(str);
            const lang = detected.language || 'text';
            return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${detected.value}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
        }
    }

    editModel(modelId) {
        const models = JSON.parse(localStorage.getItem('chatModels') || '[]');
        const model = models.find(m => m.id === modelId);

        if (model) {
            document.getElementById('editModelName').value = model.name;
            document.getElementById('editModelId').value = model.id;
            document.getElementById('editModelEndpoint').value = model.endpoint;
            document.getElementById('editApiKey').value = model.apiKey || '';
            document.getElementById('editSystemPrompt').value = model.systemPrompt || '';
            // 打开编辑模型对话框
            document.getElementById('editModelModal').style.display = 'block';
        }
    }

    saveEditModel() {
        const name = document.getElementById('editModelName').value.trim();
        const id = document.getElementById('editModelId').value.trim();
        const endpoint = document.getElementById('editModelEndpoint').value.trim();
        const apiKey = document.getElementById('editApiKey').value.trim();
        const systemPrompt = document.getElementById('editSystemPrompt').value.trim();

        if (!name || !id || !endpoint) {
            alert('请填写所有必填字段');
            return;
        }

        const models = JSON.parse(localStorage.getItem('chatModels') || '[]');
        const index = models.findIndex(m => m.id === id);

        if (index !== -1) {
            models[index] = { name, id, endpoint, apiKey, systemPrompt };
            localStorage.setItem('chatModels', JSON.stringify(models));

            this.updateModelList();
            this.updateModelSelect();
            window.closeEditModel();
        }
    }

    saveNewModel() {
        const name = document.getElementById('addModelName').value.trim();
        const id = document.getElementById('addModelId').value.trim();
        const endpoint = document.getElementById('addModelEndpoint').value.trim();
        const apiKey = document.getElementById('addApiKey').value.trim();
        const systemPrompt = document.getElementById('addSystemPrompt').value.trim();

        if (!name || !id || !endpoint) {
            alert('请填写所有必填字段');
            return;
        }

        const models = JSON.parse(localStorage.getItem('chatModels') || '[]');

        // 检查是否已存在相同ID的模型
        if (models.some(m => m.id === id)) {
            alert('已存在相同ID的模型');
            return;
        }

        models.push({ name, id, endpoint, apiKey, systemPrompt });
        localStorage.setItem('chatModels', JSON.stringify(models));

        this.updateModelList();
        this.updateModelSelect();
        window.closeAddModel();
    }

    deleteModel(modelId, showConfirm = true) {
        if (showConfirm && !confirm('确定要删除这个模型吗？')) {
            return;
        }

        const models = JSON.parse(localStorage.getItem('chatModels') || '[]');
        const filteredModels = models.filter(model => model.id !== modelId);
        localStorage.setItem('chatModels', JSON.stringify(filteredModels));

        this.updateModelList();
        this.updateModelSelect();
    }

    handleInputResizeStart(e) {
        this.isInputResizing = true;
        this.startY = e.clientY;
        this.startHeight = this.chatInput.offsetHeight;
        document.querySelector('.chat-input-area').classList.add('resizing');
        e.preventDefault();
    }

    handleInputResize(e) {
        if (!this.isInputResizing) return;

        if (this.rafId) {
            cancelAnimationFrame(this.rafId);
        }

        this.rafId = requestAnimationFrame(() => {
            const delta = this.startY - e.clientY;
            const newHeight = Math.min(Math.max(this.startHeight + delta, 60), 500);
            this.chatInput.style.height = `${newHeight}px`;
        });
    }

    handleInputResizeEnd() {
        if (this.isInputResizing) {
            this.isInputResizing = false;
            document.querySelector('.chat-input-area').classList.remove('resizing');
            if (this.rafId) {
                cancelAnimationFrame(this.rafId);
                this.rafId = null;
            }
        }
    }
}

// 初始化聊天管理器
const chatManager = new ChatManager(); 