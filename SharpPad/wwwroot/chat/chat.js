// 模型设置相关
let modelSettingsBtn, modelSettingsModal, addModelModal, addModelBtn, modelSelect;

// 聊天窗口相关
const chatPanel = document.getElementById('chatPanel');
const toggleChat = document.getElementById('toggleChat');
const chatMessages = document.getElementById('chatMessages');
const chatInput = document.getElementById('chatInput');
const clearChat = document.getElementById('clearChat');
const minimizedChatButton = document.querySelector('.minimized-chat-button');

// 添加消息历史记录存储功能
function saveChatHistory(messages) {
    localStorage.setItem('chatHistory', JSON.stringify(messages));
}

function loadChatHistory() {
    const history = localStorage.getItem('chatHistory');
    return history ? JSON.parse(history) : [];
}

// 修改 initializeChatPanel 函数
async function initializeChatPanel() {
    const chatPanel = document.getElementById('chatPanel');
    const container = document.getElementById('container');
    let isResizing = false;
    let startX;
    let startWidth;

    // 加载历史消息
    const messages = loadChatHistory();
    messages.forEach(msg => {
        addMessageToChat(msg.role, msg.content);
    });

    chatPanel.addEventListener('mousedown', (e) => {
        // 只在左边框附近 10px 范围内触发
        const leftEdge = chatPanel.getBoundingClientRect().left;
        if (Math.abs(e.clientX - leftEdge) > 10) return;

        isResizing = true;
        startX = e.clientX;
        startWidth = parseInt(document.defaultView.getComputedStyle(chatPanel).width, 10);
        chatPanel.classList.add('resizing');
        document.documentElement.style.cursor = 'ew-resize';
        e.preventDefault();  // 防止文本选择
    });

    document.addEventListener('mousemove', (e) => {
        if (!isResizing) return;

        const width = startWidth - (e.clientX - startX);
        if (width >= 450 && width <= window.innerWidth * 0.6) {  // 从300改为450
            chatPanel.style.width = `${width}px`;
            container.style.marginRight = `${width}px`;
            const fileListWidth = parseInt(getComputedStyle(fileList).width, 10);
            container.style.width = `calc(100% - ${fileListWidth}px - ${width}px)`;
            layoutEditor();
        }
    });

    document.addEventListener('mouseup', () => {
        if (isResizing) {
            isResizing = false;
            chatPanel.classList.remove('resizing');
            document.documentElement.style.cursor = '';
        }
    });

    // 当鼠标移动到左边框附近时改变光标
    chatPanel.addEventListener('mousemove', (e) => {
        const leftEdge = chatPanel.getBoundingClientRect().left;
        if (Math.abs(e.clientX - leftEdge) <= 10) {
            chatPanel.style.cursor = 'ew-resize';
        } else {
            chatPanel.style.cursor = 'default';
        }
    });

    chatPanel.addEventListener('mouseleave', () => {
        if (!isResizing) {
            chatPanel.style.cursor = 'default';
        }
    });

    // 切换聊天窗口显示/隐藏
    const toggleChat = document.getElementById('toggleChat');
    const minimizedChatButton = document.querySelector('.minimized-chat-button');

    toggleChat.addEventListener('click', () => {
        chatPanel.style.display = 'none';
        minimizedChatButton.style.display = 'block';
        container.style.marginRight = '0';
        container.style.width = `calc(100% - 290px)`;
        layoutEditor();
    });

    // 恢复聊天窗口
    minimizedChatButton.querySelector('.restore-chat').addEventListener('click', () => {
        chatPanel.style.display = 'flex';
        minimizedChatButton.style.display = 'none';
        const width = parseInt(getComputedStyle(chatPanel).width, 10);
        container.style.marginRight = `${width}px`;
        container.style.width = `calc(100% - 290px - ${width}px)`;
        layoutEditor();
    });

    // 发送消息
    const chatInput = document.getElementById('chatInput');
    const chatMessages = document.getElementById('chatMessages');

    chatInput.addEventListener('keypress', async (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            await sendChatMessage();
        }
    });

    // 清除聊天记录
    const clearChat = document.getElementById('clearChat');
    clearChat.addEventListener('click', () => {
        if (confirm('确定要清除所有聊天记录吗？此操作不可恢复。')) {
            // 清除 DOM 中的消息
            chatMessages.innerHTML = '';
            // 清除存储的消息历史
            saveChatHistory([]);
            // 显示成功通知
            showNotification('聊天记录已清除', 'success');
        }
    });

    //初始化模型选择下拉框,从localStorage中获取模型配置
    const modelConfigs = JSON.parse(localStorage.getItem('modelConfigs') || '[]');
    const modelSelect = document.getElementById('modelSelect');
    modelSelect.innerHTML = '';
    modelConfigs.forEach(model => {
        const option = document.createElement('option');
        option.value = model.id;
        option.textContent = model.name;
        modelSelect.appendChild(option);
    });
}

// 修改 sendChatMessage 函数
async function sendChatMessage() {
    const message = chatInput.value.trim();
    if (!message) return;

    // 获取当前消息历史
    const messages = loadChatHistory();
    // 添加用户消息
    messages.push({ role: 'user', content: message });
    saveChatHistory(messages);
    addMessageToChat('user', message);
    chatInput.value = '';

    try {
        // 创建一个新的消息div用于流式输出
        const messageDiv = document.createElement('div');
        messageDiv.className = 'chat-message assistant-message';
        const contentDiv = document.createElement('div');
        contentDiv.className = 'result-streaming';
        messageDiv.appendChild(contentDiv);
        chatMessages.appendChild(messageDiv);
        chatMessages.scrollTop = chatMessages.scrollHeight;

        const { reader } = await chatToLLM(messages);
        let result = "";

        // 配置 markdown-it
        const md = markdownit({
            highlight: function (str, lang) {
                if (lang && hljs.getLanguage(lang)) {
                    try {
                        const highlighted = hljs.highlight(str, { language: lang }).value;
                        return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${highlighted}</code></pre>`;
                    } catch (_) {
                        return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${md.utils.escapeHtml(str)}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                    }
                } else {
                    const detected = hljs.highlightAuto(str);
                    const lang = detected.language || 'text';
                    return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${detected.value}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                }
            }
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
                        saveChatHistory(messages);
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
                            chatMessages.scrollTop = chatMessages.scrollHeight;
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
        addMessageToChat('assistant', '抱歉，发生了错误，请稍后重试。');
        // 保存错误消息
        messages.push({ role: 'assistant', content: '抱歉，发生了错误，请稍后重试。' });
        saveChatHistory(messages);
    }
}

async function chatToLLM(messages) {
    const modelSelect = document.getElementById('modelSelect');
    const selectedModel = modelSelect.value;

    const modelConfigs = JSON.parse(localStorage.getItem('chatModels') || '[]');
    const modelConfig = modelConfigs.find(m => m.id === selectedModel);

    if (!modelConfig) {
        return new Response(JSON.stringify({
            type: 'error',
            content: '请先配置模型'
        }));
    }

    try {
        var reqMessages = messages;
        if (modelConfig.systemPrompt) {
            reqMessages = [{
                role: 'system',
                content: modelConfig.systemPrompt
            }, ...messages];
        }
        
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
    } catch (error) {
        console.error('调用AI API失败:', error);
        return new Response(JSON.stringify({
            type: 'error',
            content: '抱歉，调用AI服务失败，请检查网络连接或API配置。'
        }));
    }
}

// 在窗口大小改变时调整布局
window.addEventListener('resize', () => {
    const chatPanel = document.getElementById('chatPanel');
    const container = document.getElementById('container');

    // 确保聊天窗口宽度不超过最大限制
    const maxWidth = window.innerWidth * 0.4;
    const currentWidth = parseInt(getComputedStyle(chatPanel).width, 10);

    if (currentWidth > maxWidth) {
        const newWidth = maxWidth;
        chatPanel.style.width = `${newWidth}px`;
        container.style.marginRight = `${newWidth}px`;
        const fileListWidth = parseInt(getComputedStyle(fileList).width, 10);
        container.style.width = `calc(100% - ${fileListWidth}px - ${newWidth}px)`;
        layoutEditor();
    }
});


// 初始化模型列表
function initializeModels() {
    const defaultModels = [
    ];

    const savedModels = localStorage.getItem('chatModels');
    const models = savedModels ? JSON.parse(savedModels) : defaultModels;

    if (!savedModels) {
        localStorage.setItem('chatModels', JSON.stringify(models));
    }

    updateModelList();
    updateModelSelect();
}

// 更新模型下拉列表
function updateModelSelect() {
    const models = JSON.parse(localStorage.getItem('chatModels'));
    modelSelect.innerHTML = '';

    models.forEach(model => {
        const option = document.createElement('option');
        option.value = model.id;
        option.textContent = model.name;
        modelSelect.appendChild(option);
    });
}

// 更新模���设置列表
function updateModelList() {
    const modelList = document.getElementById('modelList');
    const models = JSON.parse(localStorage.getItem('chatModels'));

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
                <button class="edit-model" onclick="editModel('${model.id}')">编辑</button>
                <button class="delete-model" onclick="deleteModel('${model.id}')">删除</button>
            </div>
        `;
        modelList.appendChild(modelItem);
    });
}

// 关闭模型设置
function closeModelSettings() {
    modelSettingsModal.style.display = 'none';
}

// 关闭添加模型对话框
function closeAddModel() {
    document.getElementById('addModelModal').style.display = 'none';
    document.getElementById('addModelName').value = '';
    document.getElementById('addModelId').value = '';
    document.getElementById('addModelEndpoint').value = '';
    document.getElementById('addApiKey').value = '';
}

// 保存新模型
function saveNewModel() {
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

    // 检查是否已存在相同ID相同的模型
    if (models.some(m => m.id === id)) {
        alert('已存在相同ID的模型');
        return;
    }

    models.push({ name, id, endpoint, apiKey, systemPrompt });
    localStorage.setItem('chatModels', JSON.stringify(models));

    updateModelList();
    updateModelSelect();
    closeAddModel();
}

// 编辑模型
function editModel(modelId) {
    const models = JSON.parse(localStorage.getItem('chatModels'));
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

// 保存编辑的模型
function saveEditModel() {
    const name = document.getElementById('editModelName').value.trim();
    const id = document.getElementById('editModelId').value.trim();
    const endpoint = document.getElementById('editModelEndpoint').value.trim();
    const apiKey = document.getElementById('editApiKey').value.trim();
    const systemPrompt = document.getElementById('editSystemPrompt').value.trim();

    if (!name || !id || !endpoint) {
        alert('请填写所有必填字段');
        return;
    }

    const models = JSON.parse(localStorage.getItem('chatModels'));
    const index = models.findIndex(m => m.id === id);

    if (index !== -1) {
        models[index] = { name, id, endpoint, apiKey, systemPrompt };
        localStorage.setItem('chatModels', JSON.stringify(models));

        updateModelList();
        updateModelSelect();
        closeEditModel();
    }
}

// 关闭编辑模型对话框
function closeEditModel() {
    document.getElementById('editModelModal').style.display = 'none';
    document.getElementById('editModelName').value = '';
    document.getElementById('editModelId').value = '';
    document.getElementById('editModelEndpoint').value = '';
    document.getElementById('editApiKey').value = '';
    document.getElementById('editSystemPrompt').value = '';
}

// 删除模型
function deleteModel(modelId, showConfirm = true) {
    if (showConfirm && !confirm('确定要删除这个模型吗？')) {
        return;
    }

    const models = JSON.parse(localStorage.getItem('chatModels'));
    const filteredModels = models.filter(model => model.id !== modelId);
    localStorage.setItem('chatModels', JSON.stringify(filteredModels));

    updateModelList();
    updateModelSelect();
}

// 切换API Key的可见性
function toggleApiKeyVisibility(button) {
    const input = button.previousElementSibling;
    if (input.type === 'password') {
        input.type = 'text';
        button.textContent = '🔒';
    } else {
        input.type = 'password';
        button.textContent = '👁';
    }
}

// 添加消息到聊天窗口
function addMessageToChat(role, content) {
    const messageDiv = document.createElement('div');
    messageDiv.className = `chat-message ${role}-message`;
    // 如果是用户消息，直接显示文本，如果是助手消息，使用配置的markdown-it解析
    if (role === 'user') {
        messageDiv.textContent = content;
    } else {
        const md = markdownit({
            highlight: function (str, lang) {
                if (lang && hljs.getLanguage(lang)) {
                    try {
                        const highlighted = hljs.highlight(str, { language: lang }).value;
                        return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${highlighted}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                    } catch (_) {
                        return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${md.utils.escapeHtml(str)}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                    }
                } else {
                    const detected = hljs.highlightAuto(str);
                    const lang = detected.language || 'text';
                    return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${detected.value}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                }
            }
        });
        messageDiv.innerHTML = md.render(content);
    }
    chatMessages.appendChild(messageDiv);
    chatMessages.scrollTop = chatMessages.scrollHeight;
}

// 添加快捷键清除功能
document.addEventListener('keydown', (e) => {
    // Alt + L 清除聊天记录
    if (e.altKey && e.key.toLowerCase() === 'l') {
        e.preventDefault();
        const clearChat = document.getElementById('clearChat');
        clearChat.click();
    }
});

// 在页面加载完成后初始化事件监听和模型
document.addEventListener('DOMContentLoaded', async () => {
    modelSettingsBtn = document.getElementById('modelSettingsBtn');
    modelSettingsModal = document.getElementById('modelSettingsModal');
    addModelModal = document.getElementById('addModelModal');
    editModelModal = document.getElementById('editModelModal');
    addModelBtn = document.getElementById('addModelBtn');
    modelSelect = document.getElementById('modelSelect');

    // 打开模型设置
    modelSettingsBtn.addEventListener('click', () => {
        modelSettingsModal.style.display = 'block';
    });

    // 打开添加模型对话框
    addModelBtn.addEventListener('click', () => {
        addModelModal.style.display = 'block';
    });

    await initializeChatPanel();
    initializeModels();
});