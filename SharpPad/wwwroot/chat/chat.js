import { layoutEditor } from '../utils/common.js';

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

        // 聊天输入监听
        this.chatInput.addEventListener('keypress', async (e) => {
            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                await this.sendChatMessage();
            }
        });

        // 清除聊天记录按钮
        this.clearChat.addEventListener('click', () => {
            if (confirm('确定要清除所有聊天记录吗？此操作不可恢复。')) {
                this.chatMessages.innerHTML = '';
                this.saveChatHistory([]);
                this.showNotification('聊天记录已清除', 'success');
            }
        });

        // 切换聊天窗口显示/隐藏
        this.toggleChat.addEventListener('click', () => {
            this.chatPanel.style.display = 'none';
            this.minimizedChatButton.style.display = 'block';
            this.container.style.marginRight = '0';
            this.container.style.width = 'calc(100% - 290px)';
            layoutEditor();
        });

        // 恢复聊天窗口
        this.minimizedChatButton.querySelector('.restore-chat').addEventListener('click', () => {
            this.chatPanel.style.display = 'flex';
            this.minimizedChatButton.style.display = 'none';
            const width = parseInt(getComputedStyle(this.chatPanel).width, 10);
            this.container.style.marginRight = `${width}px`;
            this.container.style.width = `calc(100% - 290px - ${width}px)`;
            layoutEditor();
        });

        //模型设置
        this.modelSettingsBtn.addEventListener('click', () => {
            this.modelSettingsModal.style.display = 'block';
        });
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

        if (role === 'user') {
            messageDiv.textContent = content;
        } else {
            const md = window.markdownit({
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
        }
        this.chatMessages.appendChild(messageDiv);
        this.chatMessages.scrollTop = this.chatMessages.scrollHeight;
    }

    async sendChatMessage() {
        const message = this.chatInput.value.trim();
        if (!message) return;

        const messages = this.getChatHistory();
        messages.push({ role: 'user', content: message });
        this.saveChatHistory(messages);
        this.addMessageToChat('user', message);
        this.chatInput.value = '';

        try {
            const { reader } = await this.chatToLLM(messages);
            const messageDiv = document.createElement('div');
            messageDiv.className = 'chat-message assistant-message';
            const contentDiv = document.createElement('div');
            contentDiv.className = 'result-streaming';
            messageDiv.appendChild(contentDiv);
            this.chatMessages.appendChild(messageDiv);
            this.chatMessages.scrollTop = this.chatMessages.scrollHeight;

            let result = "";
            const md = window.markdownit({
                highlight: this.highlightCode
            });

            while (true) {
                const { value, done } = await reader.read();
                if (done) break;

                const text = new TextDecoder("utf-8").decode(value);
                const lines = text.split('\n');

                for (const line of lines) {
                    if (!line.trim() || !line.startsWith('data: ')) continue;
                    if (line === 'data: [DONE]') {
                        contentDiv.classList.remove('result-streaming');
                        if (result) {
                            messages.push({ role: 'assistant', content: result });
                            this.saveChatHistory(messages);
                        }
                        break;
                    }

                    try {
                        const data = JSON.parse(line.substring(6));
                        if (data.choices?.[0]?.delta?.content) {
                            result += data.choices[0].delta.content;
                            contentDiv.innerHTML = md.render(result);
                            this.chatMessages.scrollTop = this.chatMessages.scrollHeight;
                        }
                    } catch (err) {
                        console.error('Error parsing SSE message:', err, line);
                    }
                }
            }
        } catch (error) {
            this.addMessageToChat('assistant', '抱歉，发生了错误，请稍后重试。');
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
}

// 初始化聊天管理器
const chatManager = new ChatManager(); 