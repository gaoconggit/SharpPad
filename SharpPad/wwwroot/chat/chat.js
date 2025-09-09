import { layoutEditor, showNotification, isMobileDevice, getResponsiveSize, setContainerWidth } from '../utils/common.js';
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
        this.startY = 0;
        this.startWidth = 0;
        this.startHeight = 0;
        this.isInputResizing = false;
        this.isMobile = window.matchMedia('(max-width: 768px) and (pointer: coarse), (max-width: 480px)').matches;
        this.isInitialized = false;
        
        // 监听媒体查询变化
        window.matchMedia('(max-width: 768px) and (pointer: coarse), (max-width: 480px)').addEventListener('change', (e) => {
            this.isMobile = e.matches;
            // this.updateResizeHandle();
            // this.handleResize();
            this.updateMobileLayout();
        });

        this.initializeEventListeners();
        this.loadChatHistory();
        this.initializeModels();
        this.updateResizeHandle();
        this.initializeMobileLayout();
        
        // 初始化chatPanel显示状态监听器
        this.initializeChatPanelObserver();
        
        // 修复输入框显示问题（特别是在Mac WebView中）
        this.fixInputBoxDisplay();
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
        
        // 添加触摸事件支持
        this.chatPanel.addEventListener('touchstart', this.handleTouchStart.bind(this), { passive: false });
        document.addEventListener('touchmove', this.handleTouchMove.bind(this), { passive: false });
        document.addEventListener('touchend', this.handleTouchEnd.bind(this));

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

        // 切换聊天窗口 - 修改关闭逻辑
        this.toggleChat.addEventListener('click', () => {
            if (this.isMobile) {
                // 移动端收起
                this.chatPanel.classList.remove('active');
                
                // 等待过渡动画完成后再设置transform
                const transitionEndHandler = () => {
                    this.chatPanel.style.transform = 'translateY(100%)'; // 确保完全隐藏
                    this.chatPanel.removeEventListener('transitionend', transitionEndHandler);
                    
                    // 确保浮动按钮显示
                    setTimeout(() => {
                        if (!this.chatPanel.classList.contains('active')) {
                            this.minimizedChatButton.style.display = 'block';
                        }
                    }, 300);
                };
                
                this.chatPanel.addEventListener('transitionend', transitionEndHandler);
                
                // 移除任何可能导致部分显示的样式
                this.chatPanel.classList.remove('minimized');
                
                // 通知其他组件布局变化
                fileListResizer?.updateContainerWidth();
            } else {
                // 桌面端正常隐藏
                this.chatPanel.style.display = 'none';
                this.minimizedChatButton.style.display = 'block';
                fileListResizer.updateContainerWidth();
            }
        });

        // 恢复聊天窗口 - 修改处理逻辑支持移动端全屏
        this.minimizedChatButton.querySelector('.restore-chat').addEventListener('click', () => {
            if (this.isMobile) {
                // 移动端全屏显示
                this.chatPanel.style.display = 'flex';
                // 先清除transform样式以便过渡效果正常工作
                this.chatPanel.style.transform = 'translateY(100%)';
                
                // 强制重排以确保过渡效果
                void this.chatPanel.offsetWidth;
                
                // 添加active类并清除transform
                setTimeout(() => {
                    this.chatPanel.classList.add('active');
                    this.chatPanel.style.transform = '';
                    // 确保隐藏最小化按钮
                    this.minimizedChatButton.style.setProperty('display', 'none', 'important');
                }, 10);
                
                // 立即隐藏最小化按钮，避免过渡期间可见
                this.minimizedChatButton.style.setProperty('display', 'none', 'important');
                
                // 通知其他组件布局变化
                fileListResizer?.updateContainerWidth();

                //收起输出面板,不是display:none
                const outputPanel = document.getElementById('outputPanel');
                outputPanel.style.display = 'none';
                document.querySelector('.minimized-output-button').style.display = 'block';
                
            } else {
                // 桌面端正常显示
                this.chatPanel.style.display = 'flex';
                this.chatPanel.style.transform = '';
                this.minimizedChatButton.style.setProperty('display', 'none', 'important');
                fileListResizer.updateContainerWidth();
            }
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
        window.saveEditModel = (modelId) => this.saveEditModel(modelId);

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

        // 添加测试方法到全局，用于检查聊天面板状态
        window.checkChatPanelState = () => {
            const chatManager = this;
            const chatPanel = this.chatPanel;
            const minimizedButton = this.minimizedChatButton;
            
            console.log('Chat Panel State Check:');
            console.log('Display:', chatPanel.style.display);
            console.log('Transform:', chatPanel.style.transform);
            console.log('Has active class:', chatPanel.classList.contains('active'));
            console.log('Has minimized class:', chatPanel.classList.contains('minimized'));
            console.log('Minimized button display:', minimizedButton.style.display);
            
            // 检查状态是否一致
            const isChatVisible = (chatPanel.style.display !== 'none' && 
                !chatPanel.classList.contains('minimized') && 
                chatPanel.style.transform !== 'translateY(100%)') ||
                chatPanel.classList.contains('active');
                
            const isStateConsistent = isChatVisible ? 
                minimizedButton.style.display === 'none' : 
                minimizedButton.style.display === 'block';
                
            console.log('State consistent:', isStateConsistent);
            
            if (!isStateConsistent) {
                console.warn('Chat panel and minimized button state inconsistency detected!');
                // 自动修复状态
                if (isChatVisible) {
                    minimizedButton.style.display = 'none';
                } else {
                    minimizedButton.style.display = 'block';
                }
                console.log('State auto-corrected.');
            }
            
            return {
                chatVisible: isChatVisible,
                stateConsistent: isStateConsistent
            };
        };

        // 添加触摸手势支持 - 滑动收起面板
        let startY = 0;
        let startTime = 0;
        
        this.chatPanel.addEventListener('touchstart', (e) => {
            // 仅在聊天头部区域允许滑动
            if (e.target.closest('.chat-header')) {
                startY = e.touches[0].clientY;
                startTime = Date.now();
            }
        });
        
        this.chatPanel.addEventListener('touchmove', (e) => {
            if (startY > 0 && this.isMobile) {
                const currentY = e.touches[0].clientY;
                const deltaY = currentY - startY;
                
                // 只允许向下滑动关闭
                if (deltaY > 0) {
                    // 添加阻尼效果，最多移动屏幕高度的70%
                    const dampenedDelta = Math.min(deltaY * 0.5, window.innerHeight * 0.7);
                    this.chatPanel.style.transform = `translateY(${dampenedDelta}px)`;
                    e.preventDefault(); // 防止页面滚动
                }
            }
        });
        
        this.chatPanel.addEventListener('touchend', (e) => {
            if (startY > 0 && this.isMobile) {
                const currentY = e.changedTouches[0].clientY;
                const deltaY = currentY - startY;
                const deltaTime = Date.now() - startTime;
                
                // 如果用户滑动超过屏幕高度的20%或者速度足够快，则关闭面板
                if (deltaY > window.innerHeight * 0.2 || (deltaY > 80 && deltaTime < 300)) {
                    this.toggleChat.click();
                } else {
                    // 否则恢复面板位置
                    this.chatPanel.style.transform = '';
                }
                
                startY = 0; // 重置起始位置
            }
        });
    }

    updateResizeHandle() {
        // 添加或更新调整大小的手柄
        let resizeHandle = this.chatPanel.querySelector('.resize-handle');
        if (!resizeHandle) {
            resizeHandle = document.createElement('div');
            resizeHandle.className = 'resize-handle';
            this.chatPanel.appendChild(resizeHandle);
        }
        
        // 在移动端隐藏调整大小的手柄
        if (this.isMobile) {
            resizeHandle.style.display = 'none';
        } else {
            resizeHandle.style.display = '';
        }
    }

    handleMouseDown(e) {
        // 桌面端水平拖动
        const leftEdge = this.chatPanel.getBoundingClientRect().left;
        if (Math.abs(e.clientX - leftEdge) > 10) return;
        
        this.isResizing = true;
        this.startX = e.clientX;
        this.startWidth = parseInt(document.defaultView.getComputedStyle(this.chatPanel).width, 10);
        this.chatPanel.classList.add('resizing');
        document.documentElement.style.cursor = 'ew-resize';
        e.preventDefault();
    }

    handleTouchStart(e) {
        if (e.touches.length !== 1) return;
        
        const touch = e.touches[0];
        // 桌面端水平拖动
        const leftEdge = this.chatPanel.getBoundingClientRect().left;
        if (Math.abs(touch.clientX - leftEdge) > 20) return;
        
        this.isResizing = true;
        this.startX = touch.clientX;
        this.startWidth = parseInt(document.defaultView.getComputedStyle(this.chatPanel).width, 10);
        this.chatPanel.classList.add('resizing');
        e.preventDefault();
    }

    handleMouseMove(e) {
        if (!this.isResizing) return;

        // 桌面端水平调整大小
        const width = this.startWidth - (e.clientX - this.startX);
        if (width >= 450 && width <= window.innerWidth * 0.6) {
            this.chatPanel.style.width = `${width}px`;
            
            // 使用响应式宽度设置函数
            const fileList = document.getElementById('fileList');
            const fileListWidth = parseInt(getComputedStyle(fileList).width, 10);
            
            // 设置container的宽度和边距
            setContainerWidth(this.container, fileListWidth, width, true);
        }
    }

    handleTouchMove(e) {
        if (!this.isResizing || e.touches.length !== 1) return;
        
        const touch = e.touches[0];
        // 桌面端水平调整大小
        const width = this.startWidth - (touch.clientX - this.startX);
        if (width >= 450 && width <= window.innerWidth * 0.6) {
            this.chatPanel.style.width = `${width}px`;
            
            // 使用响应式宽度设置函数
            const fileList = document.getElementById('fileList');
            const fileListWidth = parseInt(getComputedStyle(fileList).width, 10);
            
            // 设置container的宽度和边距
            setContainerWidth(this.container, fileListWidth, width, true);
        }
        e.preventDefault();
    }

    handleMouseUp() {
        if (this.isResizing) {
            this.isResizing = false;
            this.chatPanel.classList.remove('resizing');
            document.documentElement.style.cursor = '';
        }
    }

    handleTouchEnd() {
        if (this.isResizing) {
            this.isResizing = false;
            this.chatPanel.classList.remove('resizing');
        }
    }

    handleResize() {
        this.isMobile = window.matchMedia('(max-width: 768px) and (pointer: coarse), (max-width: 480px)').matches;
        
        if (this.isMobile) {
            // 移动端适配，固定高度和宽度
            const fixedHeight = window.innerHeight * 0.85; // 固定高度为屏幕高度的85%
            this.chatPanel.style.height = `${fixedHeight}px`;
            this.chatPanel.style.top = `calc(100vh - ${fixedHeight}px)`;
            
            // 移动端下宽度始终为100%，不允许调整
            this.chatPanel.style.width = '100%';
            
            // 移除拖拽手柄，确保移动端不能调整大小
            const resizeHandle = this.chatPanel.querySelector('.resize-handle');
            if (resizeHandle) {
                resizeHandle.style.display = 'none';
            }
        } else {
            // 桌面端适配
            const maxWidthRatio = 0.4;
            const maxWidth = window.innerWidth * maxWidthRatio;
            const currentWidth = parseInt(getComputedStyle(this.chatPanel).width, 10);
            
            // 恢复拖拽手柄显示
            const resizeHandle = this.chatPanel.querySelector('.resize-handle');
            if (resizeHandle) {
                resizeHandle.style.display = '';
            }

            if (currentWidth > maxWidth) {
                const newWidth = maxWidth;
                this.chatPanel.style.width = `${newWidth}px`;
                
                // 使用响应式宽度设置函数
                const fileList = document.getElementById('fileList');
                const fileListWidth = parseInt(getComputedStyle(fileList).width, 10);
                const isChatVisible = this.chatPanel.style.display !== 'none';
                
                // 设置container的宽度和边距
                setContainerWidth(this.container, fileListWidth, newWidth, isChatVisible);
            }
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
        }).use(this.thinkBlockPlugin);
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
            let reasoning = "";

            // 配置 markdown-it
            const md = window.markdownit({
                breaks: true,
                highlight: this.highlightCode
            }).use(this.thinkBlockPlugin);

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
                            if (delta) {
                                if (delta.content) {
                                    result += delta.content;
                                }

                                if (delta.reasoning_content) {
                                    reasoning += delta.reasoning_content;
                                }

                                // 检查是否需要延时
                                const now = Date.now();
                                const timeSinceLastRender = now - lastRenderTime;
                                if (timeSinceLastRender < MIN_RENDER_INTERVAL) {
                                    await delay(MIN_RENDER_INTERVAL - timeSinceLastRender);
                                }

                                if (reasoning != "") {
                                    // 使用配置的markdown-it渲染
                                    contentDiv.innerHTML = md.render("<think>" + reasoning + "</think>\n" + result);
                                } else {
                                    // 使用配置的markdown-it渲染
                                    contentDiv.innerHTML = md.render(result);
                                }

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

        // 构建请求参数
        const requestBody = {
            model: modelConfig.name,
            messages: reqMessages,
            stream: true
        };

        // 构建请求头
        const headers = {
            'Content-Type': 'application/json',
            'Authorization': `Bearer ${modelConfig.apiKey}`,
        };

        // 使用后端调用时，请求转发到本地后端
        let endpoint = modelConfig.endpoint;
        if (modelConfig.useBackend) {
            // 如果使用后端，则将目标端点作为请求头传递
            headers['x-endpoint'] = modelConfig.endpoint;
            
            // 使用相对当前页面的API路径，自动适应任何部署环境
            endpoint = './v1/chat/completions';
        }

        const response = await fetch(endpoint, {
            method: 'POST',
            headers: headers,
            body: JSON.stringify(requestBody)
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
                    <div class="model-backend-status">${model.useBackend ? '使用后端调用' : '本地调用'}</div>
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

    thinkBlockPlugin(md) {
        function thinkBlock(state, startLine, endLine, silent) {
            const startTag = '<think>';
            const endTag = '</think>';
            let pos = state.bMarks[startLine] + state.tShift[startLine];
            let max = state.eMarks[startLine];

            // 判断本行是否以 <think> 开始
            if (state.src.slice(pos, max).trim().indexOf(startTag) !== 0) {
                return false;
            }

            // 如果仅检测，不生成 token，则返回 true
            if (silent) {
                return true;
            }

            // 找开始标签后的内容起始位置
            let contentStart = pos + startTag.length;
            let haveEndTag = false;
            let nextLine = startLine;

            // 记录 think 块的内容，在本行读取 startLine 剩余部分（若有）
            let content = state.src.slice(contentStart, max);

            // 如果本行已经包含结束标签，则只取中间文本
            if (content.indexOf(endTag) >= 0) {
                content = content.split(endTag)[0];
                haveEndTag = true;
            }

            // 否则继续读取后续行，直到找到结束标签或到达文档末尾
            while (!haveEndTag) {
                nextLine++;
                if (nextLine >= endLine) {
                    break;
                }
                pos = state.bMarks[nextLine] + state.tShift[nextLine];
                max = state.eMarks[nextLine];
                let lineText = state.src.slice(pos, max);
                if (lineText.indexOf(endTag) >= 0) {
                    // 找到结束标签，截取之前的文本
                    content += "\n" + lineText.split(endTag)[0];
                    haveEndTag = true;
                    break;
                } else {
                    content += "\n" + lineText;
                }
            }

            // 创建 token
            let token = state.push('think_open', 'div', 1);
            token.block = true;
            token.map = [startLine, nextLine + 1];
            token.attrs = [['class', 'think']];

            token = state.push('think_content', '', 0);
            token.block = true;
            token.content = content.trim();

            token = state.push('think_close', 'div', -1);
            token.block = true;

            state.line = nextLine + 1;
            return true;
        }

        // 将规则插入到 block ruler 中，放在 paragraph 规则之前
        md.block.ruler.before('paragraph', 'think_block', thinkBlock);

        md.renderer.rules.think_open = function () {
            return '<div class="think">';
        };
        md.renderer.rules.think_close = function () {
            return '</div>';
        };
        md.renderer.rules.think_content = function (tokens, idx) {
            // 默认转义 HTML，你也可以根据需求自行渲染 markdown
            //return md.utils.escapeHtml(tokens[idx].content);
            return md.render(tokens[idx].content);
        };
    }

    editModel(modelId) {
        const models = JSON.parse(localStorage.getItem('chatModels') || '[]');
        const model = models.find(m => m.id === modelId);

        if (model) {
            document.getElementById('editModelId').value = model.id;
            document.getElementById('editModelName').value = model.name;
            document.getElementById('editModelEndpoint').value = model.endpoint;
            document.getElementById('editApiKey').value = model.apiKey || '';
            document.getElementById('editSystemPrompt').value = model.systemPrompt || '';
            document.getElementById('editUseBackend').checked = model.useBackend || false;
            // 打开编辑模型对话框
            document.getElementById('editModelModal').style.display = 'block';
        }
    }

    saveEditModel(modelId) {
        const name = document.getElementById('editModelName').value.trim();
        const endpoint = document.getElementById('editModelEndpoint').value.trim();
        const apiKey = document.getElementById('editApiKey').value.trim();
        const systemPrompt = document.getElementById('editSystemPrompt').value.trim();
        const useBackend = document.getElementById('editUseBackend').checked;

        if (!name || !endpoint) {
            alert('请填写所有必填字段');
            return;
        }

        const models = JSON.parse(localStorage.getItem('chatModels') || '[]');
        const index = models.findIndex(m => m.id === modelId);

        if (index !== -1) {
            models[index] = { id: modelId, name, endpoint, apiKey, systemPrompt, useBackend };
            localStorage.setItem('chatModels', JSON.stringify(models));

            this.updateModelList();
            this.updateModelSelect();
            window.closeEditModel();
        }
    }

    saveNewModel() {
        const name = document.getElementById('addModelName').value.trim();
        const endpoint = document.getElementById('addModelEndpoint').value.trim();
        const apiKey = document.getElementById('addApiKey').value.trim();
        const systemPrompt = document.getElementById('addSystemPrompt').value.trim();
        const useBackend = document.getElementById('addUseBackend').checked;

        if (!name || !endpoint) {
            alert('请填写所有必填字段');
            return;
        }

        const models = JSON.parse(localStorage.getItem('chatModels') || '[]');

        //生成主键id,guid
        var id = crypto.randomUUID();
        models.push({ id, name, endpoint, apiKey, systemPrompt, useBackend });
        localStorage.setItem('chatModels', JSON.stringify(models));

        this.updateModelList();
        this.updateModelSelect();
        window.closeAddModel();

        //清空输入框
        document.getElementById('addModelName').value = '';
        document.getElementById('addModelEndpoint').value = '';
        document.getElementById('addApiKey').value = '';
        document.getElementById('addSystemPrompt').value = '';
        document.getElementById('addUseBackend').checked = false;
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

    // 初始化移动端布局
    initializeMobileLayout() {
        try {
            if (this.isMobile) {
                // 移动端默认收起聊天面板
                this.minimizedChatButton.style.display = 'block';
                this.chatPanel.classList.remove('active', 'minimized'); // 移除可能导致部分显示的类
                this.chatPanel.style.transform = 'translateY(100%)'; // 确保完全隐藏
                this.chatPanel.style.display = 'flex'; // 保持flex布局，但通过transform隐藏
                
                // 隐藏调整大小的手柄
                const resizeHandle = this.chatPanel.querySelector('.resize-handle');
                if (resizeHandle) {
                    resizeHandle.style.display = 'none';
                }
                
                // 确保容器宽度正确
                const container = document.getElementById('container');
                if (container) {
                    container.style.marginRight = '0';
                    container.style.width = '100%';
                }
                
                // 告知其他组件聊天面板已隐藏
                setTimeout(() => {
                    fileListResizer?.updateContainerWidth();
                }, 10);
            } else {
                // 桌面端默认显示
                this.chatPanel.style.display = 'flex';
                this.chatPanel.style.transform = ''; // 清除任何transform
                this.minimizedChatButton.style.display = 'none';
                
                // 显示调整大小的手柄
                const resizeHandle = this.chatPanel.querySelector('.resize-handle');
                if (resizeHandle) {
                    resizeHandle.style.display = '';
                }
                
                // 使用多重延迟确保布局正确应用
                setTimeout(() => {
                    fileListResizer?.updateContainerWidth();
                }, 50);
                
                // 备份延迟以防第一次失败
                setTimeout(() => {
                    fileListResizer?.updateContainerWidth();
                }, 150);
            }
            this.isInitialized = true;
        } catch (error) {
            console.error('Error initializing mobile layout:', error);
            // 发生错误时，设置一个安全的默认状态
            this.chatPanel.style.display = 'flex';
            this.minimizedChatButton.style.display = 'none';
            this.isInitialized = true;
        }
    }

    // 更新移动端布局
    updateMobileLayout() {
        if (!this.isInitialized) return;
        
        if (this.isMobile) {
            // 移动端自适应，高度固定
            this.chatPanel.style.width = '100%';
            this.chatPanel.style.height = '85vh'; // 固定高度，不允许调整
            
            // 隐藏调整大小的手柄
            const resizeHandle = this.chatPanel.querySelector('.resize-handle');
            if (resizeHandle) {
                resizeHandle.style.display = 'none';
            }
            
            // 如果当前是显示状态，确保样式正确
            if (this.chatPanel.style.display !== 'none') {
                this.chatPanel.classList.add('active');
            }
        } else {
            // 桌面端恢复默认样式
            this.chatPanel.classList.remove('active');
            
            // 显示调整大小的手柄
            const resizeHandle = this.chatPanel.querySelector('.resize-handle');
            if (resizeHandle) {
                resizeHandle.style.display = '';
            }
            
            // 设置默认宽度
            const defaultWidth = 520;
            this.chatPanel.style.width = `${defaultWidth}px`;
        }
    }


    // 添加chatPanel显示状态监听器
    initializeChatPanelObserver() {
        // 创建一个MutationObserver实例来监听chatPanel的style属性变化
        const observer = new MutationObserver((mutations) => {
            mutations.forEach((mutation) => {
                if (mutation.attributeName === 'style' || mutation.attributeName === 'class') {
                    // 检查chatPanel的显示状态
                    if ((this.chatPanel.style.display !== 'none' && 
                        !this.chatPanel.classList.contains('minimized') && 
                        this.chatPanel.style.transform !== 'translateY(100%)') ||
                        this.chatPanel.classList.contains('active')) {
                        // 当chatPanel显示时，隐藏minimized-chat-button
                        this.minimizedChatButton.style.setProperty('display', 'none', 'important');
                    } else if (this.chatPanel.style.display === 'none' || 
                              this.chatPanel.style.transform === 'translateY(100%)') {
                        // 当chatPanel隐藏时，显示minimized-chat-button
                        this.minimizedChatButton.style.display = 'block';
                    }
                }
            });
        });
        
        // 开始监听chatPanel的属性变化
        observer.observe(this.chatPanel, { 
            attributes: true, 
            attributeFilter: ['style', 'class'] 
        });
        
        // 初始化时立即检查并设置状态
        if ((this.chatPanel.style.display !== 'none' && 
            !this.chatPanel.classList.contains('minimized') && 
            this.chatPanel.style.transform !== 'translateY(100%)') ||
            this.chatPanel.classList.contains('active')) {
            // 当chatPanel显示时，隐藏minimized-chat-button
            this.minimizedChatButton.style.display = 'none';
        } else {
            // 当chatPanel隐藏时，显示minimized-chat-button
            this.minimizedChatButton.style.display = 'block';
        }
    }
    
    // 修复输入框显示问题（特别是Mac WebView中的偶发问题）
    fixInputBoxDisplay() {
        try {
            const chatInputArea = document.querySelector('.chat-input-area');
            const chatInput = document.getElementById('chatInput');
            
            if (!chatInputArea || !chatInput) {
                console.warn('Chat input elements not found, retrying in 100ms...');
                setTimeout(() => this.fixInputBoxDisplay(), 100);
                return;
            }
            
            // 确保输入区域有正确的高度和可见性
            chatInputArea.style.minHeight = '80px';
            chatInputArea.style.flexShrink = '0';
            chatInputArea.style.visibility = 'visible';
            chatInputArea.style.opacity = '1';
            chatInputArea.style.position = 'relative';
            chatInputArea.style.zIndex = '100';
            
            // 确保输入框本身正确显示
            chatInput.style.minHeight = '60px';
            chatInput.style.visibility = 'visible';
            chatInput.style.opacity = '1';
            chatInput.style.display = 'block';
            
            // 特别针对Mac WebView的修复
            if (navigator.userAgent.includes('Mac')) {
                // 强制重新计算布局
                setTimeout(() => {
                    chatInputArea.style.height = 'auto';
                    chatInputArea.offsetHeight; // 触发重排
                    
                    // 确保输入框在聊天面板完全加载后可见
                    if (this.chatPanel && getComputedStyle(this.chatPanel).opacity === '1') {
                        chatInput.style.height = 'auto';
                        chatInput.offsetHeight; // 触发重排
                    }
                }, 50);
            }
            
            console.log('Chat input box display fixed');
        } catch (error) {
            console.error('Error fixing chat input display:', error);
            // 重试一次
            setTimeout(() => this.fixInputBoxDisplay(), 200);
        }
    }
}

// 聊天管理器初始化状态跟踪
let chatInitializationState = {
    attempts: 0,
    maxAttempts: 10,
    initialized: false,
    initPromise: null
};

// 检测操作系统
function isMacOS() {
    return navigator.platform.toUpperCase().indexOf('MAC') >= 0 || 
           navigator.userAgent.toUpperCase().indexOf('MAC') >= 0;
}

// 检测是否在Avalonia WebView中运行
function isAvaloniaWebView() {
    return window.chrome && window.chrome.webview || 
           navigator.userAgent.includes('AvaloniaWebView') ||
           window.location.protocol === 'file:' ||
           window.webkit && window.webkit.messageHandlers;
}

// macOS特定的资源检查
function checkMacOSSpecificResources() {
    if (!isMacOS()) return true;
    
    // 检查WebKit特定的API是否可用
    if (window.webkit && window.webkit.messageHandlers) {
        // 在macOS WebView中，确保消息处理器已准备好
        try {
            // 测试WebKit消息处理是否正常工作
            const testMessage = JSON.stringify({test: true});
            // 不实际发送，只是测试序列化是否工作
        } catch (e) {
            console.log('WebKit message handling not ready');
            return false;
        }
    }
    
    // 检查macOS特定的CSS渲染是否完成
    const body = document.body;
    const bodyStyles = getComputedStyle(body);
    
    // macOS WebKit有时需要额外时间来计算样式
    if (bodyStyles.fontSize === '16px' || !bodyStyles.fontSize) {
        // 检查是否应用了我们的自定义样式
        const chatPanel = document.getElementById('chatPanel');
        if (chatPanel) {
            const chatStyles = getComputedStyle(chatPanel);
            if (chatStyles.backgroundColor === 'rgba(0, 0, 0, 0)' || 
                chatStyles.backgroundColor === 'transparent') {
                console.log('macOS: Custom styles not fully applied');
                return false;
            }
        }
    }
    
    return true;
}

// 全面的资源加载检查（增强macOS支持）
function checkResourcesReady() {
    // 检查document状态
    if (document.readyState !== 'complete') {
        return false;
    }
    
    // 检查关键DOM元素
    const elements = {
        chatPanel: document.getElementById('chatPanel'),
        chatInput: document.getElementById('chatInput'),
        chatMessages: document.getElementById('chatMessages'),
        minimizedButton: document.querySelector('.minimized-chat-button'),
        container: document.getElementById('container'),
        fileList: document.getElementById('fileList')
    };
    
    for (const [name, element] of Object.entries(elements)) {
        if (!element) {
            console.log(`Missing element: ${name}`);
            return false;
        }
    }
    
    // 检查CSS样式是否完全加载
    const chatPanel = elements.chatPanel;
    const computedStyles = getComputedStyle(chatPanel);
    
    // 检查关键样式属性是否已应用
    const requiredStyles = {
        width: computedStyles.width,
        height: computedStyles.height,
        position: computedStyles.position,
        display: computedStyles.display
    };
    
    for (const [prop, value] of Object.entries(requiredStyles)) {
        if (!value || value === 'auto' || value === '' || value === 'initial') {
            console.log(`Style not ready: ${prop} = ${value}`);
            return false;
        }
    }
    
    // 检查字体是否加载完成
    if (document.fonts && document.fonts.status !== 'loaded') {
        console.log('Fonts not ready:', document.fonts.status);
        return false;
    }
    
    // 检查关键CSS变量是否可用
    const rootStyles = getComputedStyle(document.documentElement);
    const chatPanelWidth = rootStyles.getPropertyValue('--chat-panel-width');
    if (!chatPanelWidth) {
        console.log('CSS variables not ready');
        return false;
    }
    
    // macOS特定检查
    if (!checkMacOSSpecificResources()) {
        return false;
    }
    
    // macOS额外检查：确保CSS动画和过渡准备就绪
    if (isMacOS()) {
        const testElement = document.createElement('div');
        testElement.style.transition = 'opacity 0.1s';
        testElement.style.opacity = '0';
        document.body.appendChild(testElement);
        
        // 强制重排
        testElement.offsetHeight;
        testElement.style.opacity = '1';
        
        const hasTransition = getComputedStyle(testElement).transitionProperty !== 'none';
        document.body.removeChild(testElement);
        
        if (!hasTransition) {
            console.log('CSS transitions not ready on macOS');
            return false;
        }
    }
    
    return true;
}

// 等待特定条件满足
function waitForCondition(conditionFn, timeout = 5000, interval = 50) {
    return new Promise((resolve, reject) => {
        const startTime = Date.now();
        
        function check() {
            if (conditionFn()) {
                resolve();
                return;
            }
            
            if (Date.now() - startTime > timeout) {
                reject(new Error('Timeout waiting for condition'));
                return;
            }
            
            setTimeout(check, interval);
        }
        
        check();
    });
}

// 强化版聊天管理器初始化
async function initializeChatManagerRobust() {
    if (chatInitializationState.initialized) {
        return window.chatManager;
    }
    
    if (chatInitializationState.initPromise) {
        return chatInitializationState.initPromise;
    }
    
    chatInitializationState.initPromise = (async () => {
        chatInitializationState.attempts++;
        
        console.log(`Chat manager initialization attempt ${chatInitializationState.attempts}/${chatInitializationState.maxAttempts}`);
        
        if (chatInitializationState.attempts > chatInitializationState.maxAttempts) {
            throw new Error('Maximum initialization attempts exceeded');
        }
        
        try {
            // macOS需要更长的等待时间
            const timeout = isMacOS() ? 5000 : 3000;
            const interval = isMacOS() ? 20 : 25;
            
            // 等待所有资源准备就绪
            await waitForCondition(checkResourcesReady, timeout, interval);
            
            // macOS额外的渲染等待时间
            const extraDelay = isMacOS() ? 50 : 10;
            await new Promise(resolve => {
                requestAnimationFrame(() => {
                    requestAnimationFrame(() => {
                        setTimeout(resolve, extraDelay);
                    });
                });
            });
            
            // macOS特殊处理：强制触发重排以确保样式计算完成
            if (isMacOS()) {
                const chatPanel = document.getElementById('chatPanel');
                if (chatPanel) {
                    // 强制重排
                    chatPanel.offsetHeight;
                    // 短暂等待
                    await new Promise(resolve => setTimeout(resolve, 20));
                }
            }
            
            // 最后一次验证
            if (!checkResourcesReady()) {
                throw new Error('Resources check failed after waiting');
            }
            
            // 初始化聊天管理器
            const chatManager = new ChatManager();
            
            // 验证初始化成功
            if (!chatManager || !chatManager.chatPanel || !chatManager.chatInput) {
                throw new Error('Chat manager initialization incomplete');
            }
            
            // 将 chatManager 暴露到全局
            window.chatManager = chatManager;
            
            // 显示聊天面板
            const chatPanel = document.getElementById('chatPanel');
            if (chatPanel) {
                setTimeout(() => {
                    chatPanel.style.opacity = '1';
                }, 20);
            }
            
            chatInitializationState.initialized = true;
            console.log('Chat manager initialized successfully on attempt', chatInitializationState.attempts);
            
            // 触发聊天管理器初始化完成事件
            window.dispatchEvent(new CustomEvent('chatManagerInitialized', {
                detail: { chatManager }
            }));
            
            return chatManager;
            
        } catch (error) {
            console.error(`Chat manager initialization failed (attempt ${chatInitializationState.attempts}):`, error);
            
            // 清除Promise引用以允许重试
            chatInitializationState.initPromise = null;
            
            if (chatInitializationState.attempts < chatInitializationState.maxAttempts) {
                // 递增延迟重试
                const delay = Math.min(100 * chatInitializationState.attempts, 1000);
                console.log(`Retrying in ${delay}ms...`);
                
                await new Promise(resolve => setTimeout(resolve, delay));
                return initializeChatManagerRobust();
            } else {
                // 最后的降级处理
                console.warn('Initializing chat manager with fallback mode');
                try {
                    const chatManager = new ChatManager();
                    window.chatManager = chatManager;
                    chatInitializationState.initialized = true;
                    return chatManager;
                } catch (fallbackError) {
                    console.error('Fallback initialization also failed:', fallbackError);
                    throw error;
                }
            }
        }
    })();
    
    return chatInitializationState.initPromise;
}

// 开始初始化过程（macOS优化版）
function startChatInitialization() {
    const isMac = isMacOS();
    const isWebView = isAvaloniaWebView();
    
    console.log(`Initializing chat manager on ${isMac ? 'macOS' : 'other OS'}${isWebView ? ' (WebView)' : ''}`);
    
    // macOS + WebView 需要特殊处理
    if (isMac && isWebView) {
        // macOS WebView 有时需要额外等待 WebKit 完全初始化
        const webViewReadyCheck = () => {
            return new Promise(resolve => {
                let attempts = 0;
                const maxAttempts = 20;
                
                const checkWebViewReady = () => {
                    attempts++;
                    
                    // 检查 WebKit API 是否完全可用
                    if (window.webkit && document.readyState === 'complete') {
                        resolve();
                        return;
                    }
                    
                    if (attempts < maxAttempts) {
                        setTimeout(checkWebViewReady, 50);
                    } else {
                        console.log('WebView readiness check timed out, proceeding anyway');
                        resolve();
                    }
                };
                
                checkWebViewReady();
            });
        };
        
        // WebView 特殊初始化流程
        webViewReadyCheck().then(() => {
            const delay = document.readyState === 'complete' ? 100 : 200;
            setTimeout(() => initializeChatManagerRobust().catch(console.error), delay);
        });
    }
    
    // 通用初始化流程（保持兼容性）
    if (document.readyState === 'complete') {
        const delay = isMac ? 50 : 1; // macOS 需要更多时间
        setTimeout(() => initializeChatManagerRobust().catch(console.error), delay);
    } else if (document.readyState === 'interactive') {
        window.addEventListener('load', () => {
            const delay = isMac ? 100 : 10;
            setTimeout(() => initializeChatManagerRobust().catch(console.error), delay);
        });
    } else {
        document.addEventListener('DOMContentLoaded', () => {
            const delay = isMac ? 50 : 10;
            setTimeout(() => initializeChatManagerRobust().catch(console.error), delay);
        });
        window.addEventListener('load', () => {
            const delay = isMac ? 150 : 50;
            setTimeout(() => initializeChatManagerRobust().catch(console.error), delay);
        });
    }
    
    // 额外的保险措施 - macOS 需要更长的备用延迟
    const backupDelay = isMac ? 2000 : 1000;
    setTimeout(() => {
        if (!chatInitializationState.initialized && !chatInitializationState.initPromise) {
            console.log(`Starting delayed initialization as backup (macOS: ${isMac})`);
            initializeChatManagerRobust().catch(console.error);
        }
    }, backupDelay);
}

// 启动初始化
startChatInitialization();

// 添加全局错误处理
window.addEventListener('error', (event) => {
    if (event.error && event.error.message && event.error.message.includes('chat')) {
        console.error('Chat-related error detected:', event.error);
        
        // 如果聊天管理器还未初始化且错误与聊天相关，尝试重新初始化
        if (!chatInitializationState.initialized && chatInitializationState.attempts < chatInitializationState.maxAttempts) {
            console.log('Attempting to recover from chat error...');
            setTimeout(() => {
                initializeChatManagerRobust().catch(console.error);
            }, 500);
        }
    }
});

// 页面可见性变化时重新验证初始化状态
document.addEventListener('visibilitychange', () => {
    if (!document.hidden && !chatInitializationState.initialized) {
        console.log('Page became visible, checking chat manager state...');
        setTimeout(() => {
            if (!window.chatManager && !chatInitializationState.initPromise) {
                console.log('Chat manager missing, attempting reinitialization...');
                initializeChatManagerRobust().catch(console.error);
            }
        }, 100);
    }
}); 