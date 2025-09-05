import { getCurrentFile } from '../utils/common.js';
import { sendRequest } from '../utils/apiService.js';
import { FileManager } from '../fileSystem/fileManager.js';

const fileManager = new FileManager();

export class CodeRunner {
    constructor() {
        this.runButton = document.getElementById('runButton');
        this.outputContent = document.getElementById('outputContent');
        this.notification = document.getElementById('notification');
        this.currentSessionId = null;
        this.initializeEventListeners();
    }

    initializeEventListeners() {
        this.runButton.addEventListener('click', () => this.runCode(window.editor.getValue()));
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
            // 如果不是 JSON，也使用 pre 元素来保持格式
            const pre = document.createElement('pre');
            pre.textContent = message;
            pre.style.margin = '0';
            pre.style.fontFamily = 'Consolas, monospace';
            pre.style.whiteSpace = 'pre-wrap';
            outputLine.appendChild(pre);
        }

        this.outputContent.appendChild(outputLine);
        this.outputContent.scrollTop = this.outputContent.scrollHeight;
    }

    streamOutput(message, type = 'info') {
        this.outputContent.classList.add("result-streaming");
        
        // 直接使用 pre 元素显示输出，保持换行符
        const outputDiv = document.createElement('div');
        outputDiv.className = `output-${type}`;
        
        const pre = document.createElement('pre');
        pre.textContent = message;
        pre.style.margin = '0';
        pre.style.fontFamily = 'Consolas, monospace';
        pre.style.whiteSpace = 'pre-wrap';
        pre.style.wordBreak = 'break-word';
        
        outputDiv.appendChild(pre);
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

    async runCode(code) {
        if (!code) return;

        //运行代码之前如果已选择文件则自动保存
        const fileId = document.querySelector('#fileListItems a.selected')?.getAttribute('data-file-id');
        if (fileId) {
            // 使用 FileManager 的公共保存方法
            fileManager.saveFileToLocalStorage(fileId, code);
        }

        // 获取当前文件的包配置
        const file = getCurrentFile();
        const packages = file?.nugetConfig?.packages || [];

        // 获取选择的C#版本
        const csharpVersion = document.getElementById('csharpVersion')?.value || 2147483647;

        // 生成会话ID
        this.currentSessionId = 'session_' + Date.now() + '_' + Math.random().toString(36).substr(2, 9);

        const request = {
            SourceCode: code,
            Packages: packages.map(p => ({
                Id: p.id,
                Version: p.version
            })),
            LanguageVersion: parseInt(csharpVersion),
            SessionId: this.currentSessionId
        };

        // 清空输出区域
        this.outputContent.innerHTML = '';

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
                            return;
                    }
                }
            }
        } catch (error) {
            this.appendOutput('运行请求失败: ' + error.message, 'error');
            this.notification.textContent = '运行失败';
            this.notification.style.backgroundColor = 'rgba(244, 67, 54, 0.9)';
            this.notification.style.display = 'block';
        }
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
}