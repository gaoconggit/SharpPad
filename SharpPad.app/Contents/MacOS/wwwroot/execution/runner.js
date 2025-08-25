import { getCurrentFile } from '../utils/common.js';
import { sendRequest } from '../utils/apiService.js';
import { FileManager } from '../fileSystem/fileManager.js';

const fileManager = new FileManager();

export class CodeRunner {
    constructor() {
        this.runButton = document.getElementById('runButton');
        this.outputContent = document.getElementById('outputContent');
        this.notification = document.getElementById('notification');
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
        const md = window.markdownit({
            highlight: function (str, lang) {
                if (lang && window.hljs.getLanguage(lang)) {
                    try {
                        const highlighted = window.hljs.highlight(str, { language: lang }).value;
                        return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${highlighted}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                    } catch (_) {
                        return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${window.markdownit().utils.escapeHtml(str)}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                    }
                } else {
                    // 如果语言不存在或者无法识别，使用自动检测
                    const detected = window.hljs.highlightAuto(str);
                    const lang = detected.language || 'text';
                    return `<pre class="hljs"><code><div class="lang-label">${lang}</div>${detected.value}</code><button class="copy-button" onclick="copyCode(this)">复制</button></pre>`;
                }
            }
        });

        this.outputContent.classList.add("result-streaming");
        const cursor = document.createElement("p");
        this.outputContent.appendChild(cursor);

        message = message.replace(/\r\n/g, '\n\r\n');
        this.outputContent.innerHTML = md.render(message);

        // 添加复制功能的样式
        const style = document.createElement('style');
        style.textContent = `
            .hljs {
                position: relative;
                padding-top: 25px;
            }
            .lang-label {
                position: absolute;
                top: 0;
                left: 0;
                padding: 2px 8px;
                font-size: 12px;
                color: #001080;
                background: #ffffff;
                border-bottom: 1px solid #d4d4d4;
                border-right: 1px solid #d4d4d4;
                border-radius: 0 0 4px 0;
            }
            .copy-button {
                position: absolute;
                top: 5px;
                right: 5px;
                padding: 4px 8px;
                background: #ffffff;
                border: 1px solid #d4d4d4;
                border-radius: 3px;
                cursor: pointer;
                opacity: 1;
                color: #001080;
                font-family: Consolas, monospace;
            }
            .copy-button:hover {
                background: #f0f0f0;
                border-color: #007acc;
            }
        `;
        document.head.appendChild(style);
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

        const request = {
            SourceCode: code,
            Packages: packages.map(p => ({
                Id: p.id,
                Version: p.version
            })),
            LanguageVersion: parseInt(csharpVersion)
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
                            result += data.content;
                            this.streamOutput(result, 'success');
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
}